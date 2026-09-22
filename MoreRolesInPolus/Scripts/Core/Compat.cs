using System.Linq;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using Virial.Assignable;

namespace MoreRolesInPolus.Scripts.Core;

public static class APICompat
{
  public static UnityEngine.Color ToUnityColor(this Virial.Color color) =>
    new UnityEngine.Color(color.R, color.G, color.B, color.A);

  public static Virial.Color ToNebulaColor(this UnityEngine.Color color) =>
    new Virial.Color(color.r, color.g, color.b, color.a);

  public static UnityEngine.Vector2 ToUnityVector(this Virial.Compat.Vector2 v) =>
    new UnityEngine.Vector2(v.x, v.y);

  public static UnityEngine.Vector3 ToUnityVector(this Virial.Compat.Vector3 v) =>
    new UnityEngine.Vector3(v.x, v.y, v.z);

  public static Virial.Compat.Vector2 ToNebulaVector(this UnityEngine.Vector2 v) =>
    new Virial.Compat.Vector2(v.x, v.y);

  public static Virial.Compat.Vector3 ToNebulaVector(this UnityEngine.Vector3 v) =>
    new Virial.Compat.Vector3(v.x, v.y, v.z);

  public static GamePlayer ToNebulaPlayer(this PlayerControl player) =>
    GamePlayer.GetPlayer(player.PlayerId)!;

  public static PlayerControl ToAUPlayer(this GamePlayer player)
  {
    throw new NotSupportedException(
      "Player.VanillaPlayer プロパティはアクセスできません。PlayerControl への変換方法を実装してください。"
    );
  }

  public static IEnumerable<T> ToEnumerable<T>(this IEnumerator<T> enumerator)
  {
    while (enumerator.MoveNext())
    {
      yield return enumerator.Current;
    }
  }

  public static void AddValueV2(this Dictionary<byte, int> self, byte target, int num)
  {
    if (self.TryGetValue(target, out var last))
      self[target] = last + num;
    else
      self[target] = num;
  }

  public static KeyValuePair<byte, int> MaxPairV2(this Dictionary<byte, int> self, out bool tie)
  {
    tie = true;
    KeyValuePair<byte, int> result = new KeyValuePair<byte, int>(PlayerVoteArea.SkippedVote, 0);
    foreach (KeyValuePair<byte, int> keyValuePair in self)
    {
      if (keyValuePair.Value > result.Value)
      {
        result = keyValuePair;
        tie = false;
      }
      else if (keyValuePair.Value == result.Value)
      {
        tie = true;
      }
    }
    return result;
  }

  /*static public AllocationParameters MoriartizedParamaters(this DefinedRole role)
  {
    if (PatchManager.MoriartizedRoleDic.TryGetValue(role.Id, out var p))
    {
      return p;
    }
    return null;

  }
  static public AllocationParameters YandereRoleParamaters(this DefinedRole role)
  {
    if (PatchManager.YandereRoleDic.TryGetValue(role.Id, out var p))
    {
      return p;
    }
    return null;
  }*/
  public static bool IsModMadmate(this GamePlayer player)
  {
    if (player.IsMadmate)
    {
      return true;
    }
    return false;
  }

  public static UnityEngine.Color RGBMultiplied(this UnityEngine.Color color, float multiplier)
  {
    return new UnityEngine.Color(
      color.r * multiplier,
      color.g * multiplier,
      color.b * multiplier,
      color.a
    );
  }

  public static UnityEngine.Color RGBMultiplied(
    this UnityEngine.Color color,
    UnityEngine.Color multiplier
  )
  {
    return new UnityEngine.Color(
      color.r * multiplier.r,
      color.g * multiplier.g,
      color.b * multiplier.b,
      color.a
    );
  }

  public static UnityEngine.Color AlphaMultiplied(this UnityEngine.Color color, float multiplier)
  {
    return new UnityEngine.Color(color.r, color.g, color.b, color.a * multiplier);
  }

  public static Virial.Color AlphaMultiplied(this Virial.Color color, float multiplier)
  {
    return new Virial.Color(color.R, color.G, color.B, color.A * multiplier);
  }

  public static FieldInfo GetPrivateFieldInfo(this object instance, string fieldname)
  {
    return instance.GetType().GetField(fieldname, BindingFlags.Instance | BindingFlags.NonPublic)!;
  }

  public static T GetPrivateField<T>(this object instance, string fieldname)
  {
    return (T)(object)instance.GetPrivateFieldInfo(fieldname).GetValue(instance)!;
  }

  public static void SetPrivateField(this object instance, string fieldname, object value)
  {
    instance.GetPrivateFieldInfo(fieldname).SetValue(instance, value);
  }

  public static MethodInfo GetPrivateMethodInfo(this object instance, string method)
  {
    if (instance is Type)
    {
      return (instance as Type)!.GetPrivateMethodInfoType(method);
    }
    return instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
  }

  public static MethodInfo GetPrivateMethodInfoType(this Type type, string method)
  {
    return type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
  }

  public static MethodInfo GetPrivateStaticMethodInfo(this object instance, string method)
  {
    return instance.GetType().GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!;
  }

  public static MethodInfo GetPrivateStaticMethodInfoType(this Type type, string method)
  {
    return type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!;
  }

  public static T CallPrivateMethod<T>(this object instance, string method, params object[] param)
  {
    return (T)(object)instance.GetPrivateMethodInfo(method).Invoke(instance, param)!;
  }

  public static T CallPrivateStaticMethod<T>(
    this object instance,
    string method,
    params object[] param
  )
  {
    return (T)(object)instance.GetPrivateStaticMethodInfo(method).Invoke(instance, param)!;
  }

  public static Type GetPrivateChildType(this Type t, string name)
  {
    return t.GetNestedType(name, BindingFlags.NonPublic)!;
  }
}
