using System.Runtime.CompilerServices;

// -----------------------------------------------------------------------------
// The core deliberately keeps some members internal: quest status may only be
// transitioned by QuestLog, not by any caller that happens to hold a QuestState.
// Widening that to public just to make it testable would remove the guarantee.
//
// Granting the test assembly access instead keeps the public API honest while
// still allowing those state machines to be verified directly.
//
// This is a compile-time hint only. Unity ignores the attribute because no
// assembly named below exists inside the editor, which is harmless.
// -----------------------------------------------------------------------------

[assembly: InternalsVisibleTo("Shadowbound.Core.Tests")]
