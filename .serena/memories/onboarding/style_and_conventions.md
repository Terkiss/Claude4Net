# Style and Conventions

- **Naming:** Follows standard .NET naming conventions (PascalCase for classes, methods, and properties; camelCase for parameters and local variables).
- **Architecture:** Interface-driven design, utilizing Dependency Injection for components.
- **Testing:** Xunit is used for unit testing.
- **Documentation:** Use of `<summary>` XML comments for public members is recommended.
- **Error Handling:** Use of `IsError` flags in result objects (e.g., `ToolUseResult`, `McpCallToolResult`) along with standard exceptions.
