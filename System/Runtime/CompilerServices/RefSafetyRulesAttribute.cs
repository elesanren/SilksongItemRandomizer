// Decompiled with JetBrains decompiler
// Type: System.Runtime.CompilerServices.RefSafetyRulesAttribute
// Assembly: SilksongItemRandomizer, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null
// MVID: B70CDFC5-FCC7-47DC-924E-17AA884F2058
// Assembly location: E:\a\HardItemRandomizer\plugins\SilksongItemRandomizer.dll

using Microsoft.CodeAnalysis;
using System.Runtime.InteropServices;

#nullable disable
namespace System.Runtime.CompilerServices;

[CompilerGenerated]
[Embedded]
[AttributeUsage(AttributeTargets.Module, AllowMultiple = false, Inherited = false)]
internal sealed class RefSafetyRulesAttribute : Attribute
{
  public readonly int Version;

  public RefSafetyRulesAttribute([In] int obj0) => this.Version = obj0;
}
