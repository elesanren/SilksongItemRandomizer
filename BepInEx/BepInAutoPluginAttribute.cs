// Decompiled with JetBrains decompiler
// Type: BepInEx.BepInAutoPluginAttribute
// Assembly: SilksongItemRandomizer, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
// MVID: B70CDFC5-FCC7-47DC-924E-17AA884F2058
// Assembly location: E:\a\HardItemRandomizer\plugins\SilksongItemRandomizer.dll

using System;
using System.Diagnostics;

#nullable enable
namespace BepInEx;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
[Conditional("CodeGeneration")]
internal sealed class BepInAutoPluginAttribute : Attribute
{
  public BepInAutoPluginAttribute(string? id = null, string? name = null, string? version = null)
  {
  }
}
