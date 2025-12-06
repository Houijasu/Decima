# Task Completion Checklist

When completing a coding task in Decima, ensure the following:

## Before Committing
1. **Build succeeds:** `dotnet build` completes without errors
2. **No warnings:** Address or acknowledge any compiler warnings
3. **Code style:** Follow conventions in `.editorconfig`
   - File-scoped namespaces
   - Proper naming conventions
   - Using directives inside namespace

## Code Quality
- [ ] Nullable reference types handled properly
- [ ] No unused imports
- [ ] Public APIs have XML documentation (if applicable)
- [ ] Error handling in place (especially for file I/O and user input)

## For ML/TorchSharp Code
- [ ] Dispose tensors properly to avoid memory leaks
- [ ] Handle CUDA availability gracefully (fallback to CPU)
- [ ] Use appropriate data types (float, long, etc.)

## For CLI Commands
- [ ] Command inherits from `Command<TSettings>`
- [ ] Settings class has proper attributes (`[CommandOption]`, `[CommandArgument]`)
- [ ] Include `[Description]` for help text
- [ ] Validate user input appropriately
- [ ] Use Spectre.Console for formatted output

## Testing (when implemented)
- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
