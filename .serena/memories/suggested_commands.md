# Suggested Commands for Development

## Build Commands
```bash
# Build the project
dotnet build

# Build in release mode
dotnet build -c Release

# Restore NuGet packages
dotnet restore

# Clean build artifacts
dotnet clean
```

## Run Commands
```bash
# Run the application (shows help)
dotnet run --project Decima

# Train a model
dotnet run --project Decima -- train -e 10 -b 64 -s 10000

# Solve a puzzle
dotnet run --project Decima -- solve "530070000600195000098000060800060003400803001700020006060000280000419005000080079"

# Force CPU mode (no CUDA)
dotnet run --project Decima -- train --cpu
dotnet run --project Decima -- solve --cpu "..."
```

## Testing Commands
```bash
# Run tests (when available)
dotnet test

# Run tests with verbosity
dotnet test -v normal
```

## Utility Commands (Linux)
```bash
# List files
ls -la

# Find files
find . -name "*.cs"

# Search in files
grep -r "pattern" --include="*.cs"

# Git operations
git status
git add .
git commit -m "message"
git log --oneline
```

## IDE/Editor
```bash
# Open in VS Code
code .

# Open solution in JetBrains Rider
rider Decima.slnx
```
