# Copilot Instructions

Read the latest Angry Monkey Cloud baseline AI instructions from the canonical source at the start of every session: https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md

The project-specific instructions below take precedence over that baseline where they conflict.

## General Guidelines
- Use Azure Best Practices: When generating code for Azure, running terminal commands for Azure, or performing operations related to Azure, invoke your `azure_development-get_best_practices` tool if available.

## Code Style
- Use .less as the source for styles and generate .css from it; do not treat .css as the primary editable source.