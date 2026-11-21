using System.Diagnostics;

// The HUD compiles source plugins in Debug: without this the JIT runs every
// method unoptimized (~2-4x slower). Sequence points are kept for stack
// traces; only the "disable optimizations" flag is cleared.
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
