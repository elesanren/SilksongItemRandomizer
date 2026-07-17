// Decompiled with JetBrains decompiler
// Type: BepInEx.Preloader.Core.Patching.PatcherAutoPluginAttribute
// Assembly: SilksongItemRandomizer, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
// MVID: B70CDFC5-FCC7-47DC-924E-17AA884F2058
// Assembly location: E:\a\HardItemRandomizer\plugins\SilksongItemRandomizer.dll

using System;
using System.Diagnostics;

#nullable enable
namespace BepInEx.Preloader.Core.Patching;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
[Conditional("CodeGeneration")]
internal sealed class PatcherAutoPluginAttribute : Attribute
{
  public PatcherAutoPluginAttribute(string? id = null, string? name = null, string? version = null)
  {
  }
}
