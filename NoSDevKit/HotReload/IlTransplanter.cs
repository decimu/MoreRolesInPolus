using System.Reflection;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;

namespace NoSDevKit.HotReload;

/// <summary>
/// 新しくコンパイルしたメソッド本体の IL を、実行中の古いメソッドへ移植する。
/// 新しい DLL はロードせず、IL 中の参照はすべて実行中のアセンブリのメンバに解決し直す。
/// </summary>
internal class IlTransplanter
{
    /// <summary>移植対象 1 メソッド分の準備結果。operand はすべて解決済み</summary>
    internal class Plan
    {
        public MethodBase Target = null!;
        public MethodDefinition Source = null!;
        public string BodySignature = null!;
        public Dictionary<object, object> Resolved = null!;
    }

    /// <summary>
    /// 型・フィールド・メソッドのシグネチャ一覧。これが一致しない場合はメソッド本体の差し替えでは対応できない
    /// </summary>
    public static HashSet<string> StructureOf(ModuleDefinition module)
    {
        HashSet<string> result = [];
        foreach (var type in module.GetTypes())
        {
            // 定数配列などの初期化データ。中身が変わると名前も変わるが、参照の解決で検出できる
            if (type.Name.StartsWith("<PrivateImplementationDetails>")) continue;

            result.Add($"T:{type.FullName} : {type.BaseType?.FullName}");
            foreach (var f in type.Fields) result.Add($"F:{(f.IsStatic ? "static " : "")}{f.FullName}");
            foreach (var m in type.Methods) result.Add($"M:{(m.IsStatic ? "static " : "")}{m.FullName}");
        }
        return result;
    }

    /// <summary>
    /// メソッド本体の比較用文字列。トークン値に依存しないので、別々にコンパイルした DLL 同士でも比較できる
    /// </summary>
    public static string BodySignatureOf(MethodDefinition method)
    {
        if (!method.HasBody) return "";
        var body = method.Body;
        var sb = new StringBuilder();
        foreach (var v in body.Variables) sb.Append("V:").Append(v.VariableType.FullName).Append('\n');
        var index = new Dictionary<Instruction, int>();
        for (int i = 0; i < body.Instructions.Count; i++) index[body.Instructions[i]] = i;
        foreach (var ins in body.Instructions)
        {
            sb.Append(ins.OpCode.Name).Append(' ');
            sb.Append(ins.Operand switch
            {
                Instruction t => "@" + index[t],
                Instruction[] ts => string.Join(",", ts.Select(t => "@" + index[t])),
                VariableDefinition v => "v" + v.Index,
                ParameterDefinition p => "p" + p.Index,
                MemberReference r => r.FullName,
                string s => "\"" + s + "\"",
                null => "",
                var o => o.ToString(),
            });
            sb.Append('\n');
        }
        foreach (var h in body.ExceptionHandlers)
            sb.Append($"H:{h.HandlerType} {index[h.TryStart]} {(h.TryEnd != null ? index[h.TryEnd] : -1)} {index[h.HandlerStart]} {(h.HandlerEnd != null ? index[h.HandlerEnd] : -1)} {(h.FilterStart != null ? index[h.FilterStart] : -1)} {h.CatchType?.FullName}\n");
        return sb.ToString();
    }

    private static object ResolveMember(MemberReference reference)
    {
        var resolved = reference.ResolveReflection();
        if (resolved == null) throw new InvalidOperationException($"実行中のアセンブリに見つかりません: {reference.FullName}");
        return resolved;
    }

    /// <summary>
    /// 移植の準備。参照をすべて解決できなければ例外を投げる（何も変更しない）
    /// </summary>
    public static Plan Prepare(MethodDefinition source, string bodySignature)
    {
        if (source.HasGenericParameters || source.DeclaringType.HasGenericParameters)
            throw new NotSupportedException("ジェネリックなメソッド・型は差し替えに対応していません");

        var target = (MethodBase)ResolveMember(source);
        var resolved = new Dictionary<object, object>();

        void Resolve(MemberReference? r)
        {
            if (r == null || resolved.ContainsKey(r)) return;
            resolved[r] = ResolveMember(r);
        }

        foreach (var v in source.Body.Variables) Resolve(v.VariableType);
        foreach (var h in source.Body.ExceptionHandlers) Resolve(h.CatchType);
        foreach (var ins in source.Body.Instructions)
        {
            switch (ins.Operand)
            {
                case MemberReference r: Resolve(r); break;
                case CallSite: throw new NotSupportedException("calli 命令には対応していません");
            }
        }

        return new Plan { Target = target, Source = source, BodySignature = bodySignature, Resolved = resolved };
    }

    /// <summary>
    /// ILHook の中で呼ばれ、対象メソッドの本体を丸ごと置き換える
    /// </summary>
    public static void Transplant(ILContext il, Plan plan)
    {
        var src = plan.Source.Body;
        var dst = il.Body;

        TypeReference ImportType(TypeReference t) => il.Import((Type)plan.Resolved[t]);

        dst.Instructions.Clear();
        dst.ExceptionHandlers.Clear();
        dst.Variables.Clear();
        dst.InitLocals = src.InitLocals;

        var variables = src.Variables.Select(v =>
        {
            var nv = new VariableDefinition(ImportType(v.VariableType));
            dst.Variables.Add(nv);
            return nv;
        }).ToList();

        // 引数は「this を含めた位置」で対応づける（ILHook 側では this が明示引数になっている場合がある）
        List<ParameterDefinition> srcArgs = [];
        if (plan.Source.HasThis) srcArgs.Add(src.ThisParameter);
        srcArgs.AddRange(plan.Source.Parameters);
        List<ParameterDefinition> dstArgs = [];
        if (il.Method.HasThis) dstArgs.Add(dst.ThisParameter);
        dstArgs.AddRange(il.Method.Parameters);
        if (srcArgs.Count != dstArgs.Count) throw new InvalidOperationException($"引数の数が一致しません: {plan.Source.FullName}");

        var map = new Dictionary<Instruction, Instruction>();
        foreach (var ins in src.Instructions) map[ins] = Instruction.Create(OpCodes.Nop);

        foreach (var ins in src.Instructions)
        {
            var n = map[ins];
            n.OpCode = ins.OpCode;
            n.Operand = ins.Operand switch
            {
                Instruction t => map[t],
                Instruction[] ts => ts.Select(t => map[t]).ToArray(),
                VariableDefinition v => variables[v.Index],
                ParameterDefinition p => dstArgs[srcArgs.IndexOf(p)],
                TypeReference t => ImportType(t),
                FieldReference f => il.Import((FieldInfo)plan.Resolved[f]),
                MethodReference m => il.Import((MethodBase)plan.Resolved[m]),
                var o => o,
            };
            dst.Instructions.Add(n);
        }

        foreach (var h in src.ExceptionHandlers)
        {
            dst.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType)
            {
                TryStart = map[h.TryStart],
                TryEnd = h.TryEnd != null ? map[h.TryEnd] : null,
                HandlerStart = map[h.HandlerStart],
                HandlerEnd = h.HandlerEnd != null ? map[h.HandlerEnd] : null,
                FilterStart = h.FilterStart != null ? map[h.FilterStart] : null,
                CatchType = h.CatchType != null ? ImportType(h.CatchType) : null,
            });
        }
    }
}
