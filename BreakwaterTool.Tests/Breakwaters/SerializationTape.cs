using System;
using System.Collections.Generic;
using System.Reflection;
using Colossal.Serialization.Entities;

namespace BreakwaterTool.Tests.Breakwaters;

/// <summary>
/// <para>
/// A fake of the game's <see cref="IWriter"/> that records each value written, in order.
/// </para>
/// <para>
/// The writer has some forty members, so <see cref="DispatchProxy"/> implements them all and
/// routes each call to <see cref="Invoke"/>: a <c>Write</c> is recorded, any other call throws.
/// Public and unsealed, as <see cref="DispatchProxy"/> derives the proxy from it.
/// </para>
/// </summary>
public class TapeWriter : DispatchProxy {
  internal List<object> Values { get; } = [];

  internal static (IWriter Writer, TapeWriter Tape) Create() {
    var writer = DispatchProxy.Create<IWriter, TapeWriter>();

    return (writer, (TapeWriter) writer);
  }

  protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) {
    ArgumentNullException.ThrowIfNull(targetMethod);

    if (targetMethod.Name != nameof(IWriter.Write) || args is not [{} value]) {
      throw new NotImplementedException(targetMethod.ToString());
    }

    this.Values.Add(value);

    return null;
  }
}

/// <summary>
/// A fake of the game's <see cref="IReader"/> that reads back the values a <see cref="TapeWriter"/>
/// recorded, each one into an <c>out</c> parameter of the type it was written as.
/// Public and unsealed, as <see cref="DispatchProxy"/> derives the proxy from it.
/// </summary>
public class TapeReader : DispatchProxy {
  private Queue<object> values = null!;

  internal int Left => this.values.Count;

  internal static (IReader Reader, TapeReader Tape) Create(IEnumerable<object> values) {
    var reader = DispatchProxy.Create<IReader, TapeReader>();
    var tape = (TapeReader) reader;

    tape.values = new Queue<object>(values);

    return (reader, tape);
  }

  protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) {
    ArgumentNullException.ThrowIfNull(targetMethod);

    var parameters = targetMethod.GetParameters();

    if (targetMethod.Name != nameof(IReader.Read) || args is not [_] || !parameters[0].IsOut) {
      throw new NotImplementedException(targetMethod.ToString());
    }

    var value = this.values.Dequeue();
    var type = parameters[0].ParameterType.GetElementType();

    if (value.GetType() != type) {
      throw new InvalidOperationException($"Read a {type} where a {value.GetType()} was written.");
    }

    args[0] = value;

    return null;
  }
}
