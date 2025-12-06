# Code Style and Conventions

## General
- Indentation: 4 spaces for C# files
- Charset: UTF-8 with BOM for C# files
- Final newline: required

## C# Specific

### Namespaces
- **File-scoped namespaces** are required (`namespace Foo;` not `namespace Foo { }`)
- Using directives placed **inside** namespace blocks

### Naming Conventions
- **PascalCase:** Classes, methods, properties, public/protected fields, constants, local functions
- **camelCase:** Local variables, parameters
- **_camelCase:** Private instance fields (prefix with `_`)
- **s_camelCase:** Private static fields (prefix with `s_`)

### Types and Variables
- Prefer `var` keyword everywhere when type is apparent
- Enable nullable reference types (`<Nullable>enable</Nullable>`)
- Use language keywords over framework types (`int` not `Int32`)

### Formatting
- Braces on new lines (Allman style)
- Space after keywords in control flow (`if (`, `for (`, etc.)
- No space after cast: `(int)value`
- Prefer braces for all blocks

### Modern Language Features
- Use pattern matching (`is`, `switch` expressions)
- Use collection initializers and object initializers
- Use null-coalescing and null-propagation operators
- Use expression-bodied members for properties/indexers

### Imports
- Sort System.* directives first
- Separate import groups with blank line

## File Header
Files should include MIT license header (optional but defined in .editorconfig):
```csharp
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
```
