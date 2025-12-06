# Workflow Process for Coding Tasks

Follow this structured workflow when working on coding tasks:

## Step 1: Gather Information & Reflect
- Search and read relevant code/files
- **Use Serena MCP Thinking tools:** `think_about_collected_information`
- Ensure you have sufficient context before proceeding

## Step 2: Plan with Sequential Thinking
- **Use Sequential Thinking MCP:** `sequential-thinking_sequentialthinking`
- Break down the problem into logical steps
- Generate and verify hypotheses
- Adjust plan as understanding deepens

## Step 3: Validate Approach with Vibe Check
- **Use Vibe Check MCP:** `vibe_check`
- Identify assumptions and potential blind spots
- Prevent tunnel vision and cascading errors
- Log any learnings with `vibe_learn`

## Step 4: Execute File Edits
- **Use Serena MCP Thinking tools:** `think_about_task_adherence`
- Proceed with code editing (symbolic or file-based)
- Follow code style conventions
- Make targeted, precise changes

## Step 5: Build
- **Build the project:** `dotnet build`
- If errors or warnings exist → fix them
- Repeat until build is clean

## Step 6: Format Code
- **Run:** `dotnet format`
- Ensure code follows project style conventions

## Step 7: Verify Completion
- **Use Serena MCP Thinking tools:** `think_about_whether_you_are_done`
- Confirm all requirements are met
- Follow task completion checklist

## Summary Flow
```
1. Gather Info → think_about_collected_information
2. Plan       → sequential_thinking
3. Validate   → vibe_check
4. Edit       → think_about_task_adherence → make changes
5. Build      → dotnet build → fix errors/warnings
6. Format     → dotnet format
7. Done?      → think_about_whether_you_are_done
```
