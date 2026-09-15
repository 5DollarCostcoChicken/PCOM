# PCOM Development Instructions

## Project

PCOM, also called Polynex, is a turn-based medieval-fantasy tactics game
inspired by XCOM and Star Wars: Zero Company.

When the design documentation does not specify a tactical mechanic, use
Zero Company as the provisional design reference, while adapting ranged
concepts such as overwatch to PCOM's greater emphasis on melee combat,
stances, engagement, reactions, and positioning.

Other markdown files that are relevant, located in the same folder as this, is DESIGNDOC.md

## Unity

- This is a Unity C# project in Unity 6.3 LTS (6000.3.15f1).
- Treat the repository root as the Unity project root.
- Keep runtime code under `Assets/PCOM/Scripts/Runtime`.
- Keep editor-only code under an `Editor` directory.
- Keep tests under `Assets/PCOM/Scripts/Tests`.
- Preserve `.meta` files.
- Do not manually edit generated `.csproj` or `.sln` files.
- Do not modify `Library`, `Temp`, `Logs`, or `Obj`.
- Do not add packages or change the Unity version without asking first.
- Do not modify scenes or prefabs as raw YAML unless the task requires it
  and the change can be reviewed safely.

## Architecture

- Prefer plain C# domain logic where Unity APIs are unnecessary.
- Keep MonoBehaviours focused on scene integration and presentation.
- Prefer composition over deep inheritance.
- Avoid global static mutable state.
- Use ScriptableObjects for authored content, definitions, and configuration.
- Keep runtime battle state separate from ScriptableObject source assets.
- Separate combat rules, presentation, input, and AI.
- All combat resolution should be deterministic when supplied the same
  state, commands, and random seed.
- Use structured result objects for damage, avoidance, status effects,
  movement, reactions, and other combat outcomes.
- Centralize turn scheduling rather than letting individual units compete
  through independent Update methods.
- Use team IDs and control strategies rather than separate player and enemy
  unit implementations.

## C# Style

- Use explicit, descriptive names.
- Use namespaces beginning with `PCOM`.
- One primary public type per file.
- Avoid LINQ in performance-sensitive update loops.
- Avoid allocations in Update, FixedUpdate, and frequently executed combat paths.
- Use `[SerializeField] private` for inspector fields rather than public fields.
- Validate public method arguments where failure would otherwise be obscure.
- Add XML documentation where an API's contract is not obvious from its name.
- Do not perform unrelated refactors during focused changes.

## Workflow

Before editing:

1. Inspect the relevant callers, interfaces, tests, and serialized dependencies.
2. Explain assumptions when the design is ambiguous.
3. Prefer the smallest coherent change.

After editing:

1. Review the diff.
2. Check for Unity serialization or assembly-definition consequences.
3. Run the narrowest available tests.
4. Report what changed, what was validated, and anything requiring Unity Editor verification.

## Safety

- Never delete user-authored assets merely because they appear unused.
- Never regenerate GUIDs intentionally.
- Never rename or move Unity assets without preserving their `.meta` files.
- Ask before making sweeping architecture changes.
- Treat compiler errors as blockers; do not hide them with warning suppression.

## Test Command

"C:\Users\kiran\OneDrive\Desktop\Unity\6000.3.15f1\Editor\Unity.exe" `
  -batchmode `
  -nographics `
  -quit `
  -projectPath "C:\UnityDevelopment\PCOM" `
  -runTests `
  -testPlatform EditMode `
  -testResults "C:\UnityDevelopment\PCOM\TestResults.xml" `
  -logFile "C:\UnityDevelopment\PCOM\EditModeTests.log"