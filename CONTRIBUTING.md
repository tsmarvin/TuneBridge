# Contributing to BridgeBeats

Thank you for your interest in contributing to BridgeBeats! 🎵

BridgeBeats is a cross-platform music link converter that helps people share music effortlessly across streaming platforms. Whether you're fixing a bug, adding a feature, improving documentation, or helping others, your contributions make BridgeBeats better for everyone.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Security](#security)
- [Getting Help](#getting-help)
- [Ways to Contribute](#ways-to-contribute)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Submitting Issues](#submitting-issues)
- [Submitting Pull Requests](#submitting-pull-requests)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Documentation Guidelines](#documentation-guidelines)

## Code of Conduct

This project adheres to the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior as described in the Code of Conduct.

## Security

Security is a top priority for BridgeBeats.

### Reporting Security Vulnerabilities

**Never open public issues for security vulnerabilities.** Instead:

1. **Follow our [Security Policy](SECURITY.md)** for reporting instructions and contact information
2. **Alternatively, create a private security advisory** at [GitHub Security](https://github.com/tsmarvin/BridgeBeats/security/advisories/new)
3. **Include**: Description, impact, reproduction steps, affected versions

See our complete [Security Policy](SECURITY.md) for details and response timelines.

### Security Best Practices for Contributors

- **Never commit secrets** - use environment variables
- **Validate all inputs** - prevent injection attacks
- **Update dependencies** - keep libraries current (Dependabot helps)
- **Review security advisories** - stay informed about vulnerabilities
- **Test security features** - verify authentication and authorization work correctly

## Getting Help

Need help or have questions?

### Documentation

- 📚 **[Documentation Site](https://docs.bridgebeats.link)** - Comprehensive guides
- 📖 **[README](README.md)** - Project overview and quick start
- ⚙️ **[Configuration Guide](docs/CONFIGURATION.md)** - Setup instructions
- 🚀 **[Deployment Guide](docs/DEPLOYMENT.md)** - Production deployment
- 🧑‍💻 **[Local Development Guide](docs/LOCAL_DEVELOPMENT.md)** - Development setup

### Getting Support

- **Questions about usage**: Search or open a [GitHub Issue](https://github.com/tsmarvin/BridgeBeats/issues)
- **Bugs or problems**: Use the [Bug Report template](https://github.com/tsmarvin/BridgeBeats/issues/new?template=bug_report.yml)
- **Feature ideas**: Use the [Feature Request template](https://github.com/tsmarvin/BridgeBeats/issues/new?template=feature_request.yml)
- **Security issues**: Follow the [Security Policy](SECURITY.md)

### Community

- **Be respectful**: Follow our [Code of Conduct](CODE_OF_CONDUCT.md)
- **Be patient**: Maintainers and contributors are often volunteers
- **Be helpful**: Share your knowledge and help others
- **Be open**: Welcome feedback and different perspectives

## Ways to Contribute

There are many ways to contribute to BridgeBeats:

### 🐛 Report Bugs

Found a bug? [Submit a bug report](https://github.com/tsmarvin/BridgeBeats/issues/new?template=bug_report.yml) using our bug report template. Please include:
- Clear description of the issue
- Steps to reproduce
- Expected vs. actual behavior
- Environment details (OS, .NET version, deployment method)
- Relevant logs or error messages

### ✨ Suggest Features

Have an idea for a new feature? [Submit a feature request](https://github.com/tsmarvin/BridgeBeats/issues/new?template=feature_request.yml). Tell us:
- What problem you're trying to solve
- Your proposed solution
- Which component(s) would be affected
- Any use cases or examples

### 📚 Improve Documentation

Documentation improvements are always welcome! This includes:
- Fixing typos or unclear instructions
- Adding missing documentation
- Creating examples or tutorials
- Improving code comments
- Updating outdated information

[Submit a documentation issue](https://github.com/tsmarvin/BridgeBeats/issues/new?template=documentation.yml) or open a PR directly for minor fixes.

### 🔒 Report Security Vulnerabilities

**Do not open public issues for security vulnerabilities.** See our [Security Policy](SECURITY.md) for how to report security issues responsibly.

### 🧪 Test and Review

Help by:
- Testing pre-release versions
- Reviewing pull requests
- Verifying bug fixes
- Testing on different platforms or configurations

### 💬 Help Others

Assist other users by:
- Answering questions in issues and discussions
- Sharing your experience and best practices
- Creating tutorials or blog posts
- Improving the community

## Getting Started

Before you start contributing:

1. **Search existing issues** to see if your topic has already been discussed
2. **Read the documentation** at [https://docs.bridgebeats.link](https://docs.bridgebeats.link)
3. **Set up your development environment** (see below)
4. **Familiarize yourself with the codebase** by exploring the project structure

## Development Setup

### Prerequisites

- **.NET 10.0 SDK or later** - [Download](https://dotnet.microsoft.com/download/dotnet/10)
- **Docker Desktop** (for containerized dependencies and testing)
- **Git** for version control
- **IDE** - Visual Studio 2026, VS Code, or Rider (recommended)

### Initial Setup

1. **Fork the repository** on GitHub
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/YOUR-USERNAME/BridgeBeats.git
   cd BridgeBeats
   ```

3. **Add upstream remote**:
   ```bash
   git remote add upstream https://github.com/tsmarvin/BridgeBeats.git
   ```

4. **Configure API credentials** (optional for some contributions):
   - Use `dotnet user-secrets` for sensitive data (recommended for development)
   - Or copy `.env.example` to `.env` and add your credentials (for Docker deployments)
   - See [Configuration Guide](docs/CONFIGURATION.md) for details

### Running the Application

Use .NET Aspire for local development:

```bash
aspire run
```

This starts the application with the Aspire Dashboard for monitoring, tracing, and structured logging. See the [Local Development Guide](docs/LOCAL_DEVELOPMENT.md) for more details.

### Running Tests

```bash
# Run all tests
dotnet test

# Run only unit tests (no API credentials needed)
dotnet test --filter "FullyQualifiedName~Unit"

# Run integration tests (requires API credentials)
dotnet test --filter "FullyQualifiedName~Integration"

# Run end-to-end tests
dotnet test --filter "FullyQualifiedName~EndToEnd"
```

## Submitting Issues

We use GitHub Issues to track bugs, feature requests, and documentation issues. Before submitting:

1. **Search existing issues** to avoid duplicates
2. **Check the documentation** to see if your question is already answered
3. **Use the appropriate issue template**:
   - [🐛 Bug Report](https://github.com/tsmarvin/BridgeBeats/issues/new?template=bug_report.yml)
   - [✨ Feature Request](https://github.com/tsmarvin/BridgeBeats/issues/new?template=feature_request.yml)
   - [📚 Documentation Issue](https://github.com/tsmarvin/BridgeBeats/issues/new?template=documentation.yml)

### Writing Good Issues

- **Use a clear, descriptive title** that summarizes the issue
- **Provide complete information** using the issue template
- **Include code samples, logs, or screenshots** when relevant
- **Be specific** about what you expected vs. what actually happened
- **One issue per report** - don't combine multiple unrelated issues

## Submitting Pull Requests

We welcome pull requests for bug fixes, features, and documentation improvements!

### Before You Start

1. **Check for existing PRs** to avoid duplicate work
2. **Open an issue first** for significant changes to discuss the approach
3. **Keep changes focused** - one PR per bug fix or feature
4. **Follow our coding standards** (see below)

### Pull Request Process

1. **Create a feature branch** from `main`:
   ```bash
   git checkout -b feature/your-feature-name
   # or
   git checkout -b fix/your-bug-fix
   ```

2. **Make your changes**:
   - Write clear, concise code
   - Follow existing code style and conventions
   - Add or update tests as needed
   - Update documentation if applicable

3. **Test your changes**:
   ```bash
   dotnet build
   dotnet test
   ```

4. **Commit your changes** with clear commit messages:
   ```bash
   git add .
   git commit -m "Add feature: brief description"
   ```

5. **Push to your fork**:
   ```bash
   git push origin feature/your-feature-name
   ```

6. **Open a Pull Request** using our [PR template](.github/PULL_REQUEST_TEMPLATE.md):
   - Provide a clear description of the changes
   - Link to related issues with "Fixes #123"
   - Select the appropriate change type
   - Complete the checklist

### Pull Request Guidelines

- **Keep PRs small and focused** - easier to review and merge
- **Write descriptive PR titles** that summarize the change
- **Reference related issues** in the PR description
- **Respond to review feedback** promptly and professionally
- **Keep your branch updated** with the latest `main` branch
- **Ensure CI passes** - all tests and checks must pass
- **Don't commit secrets** - use environment variables, dotnet secrets, or GitHub secrets

### After Submitting

- Be patient - maintainers will review your PR as soon as possible
- Be responsive to feedback and questions
- Make requested changes in new commits (don't force-push)
- Once approved, your PR will be merged by a maintainer

## Coding Standards

BridgeBeats follows .NET coding conventions and includes specific style guidelines.

### General Conventions

- **PascalCase**: Classes, methods, properties, constants, public fields, namespaces
- **camelCase with underscore prefix**: Instance fields (e.g., `_fieldName`)
- **camelCase with s_ prefix**: Static fields (e.g., `s_staticField`)
- **camelCase**: Local variables, parameters, local function parameters

### Code Style

- **Indentation**: 4 spaces for C# files, 2 spaces for JSON/XML/Shell scripts
- **Braces**: OTBS (One True Brace Style) - opening braces on same line
- **Line endings**: LF (Unix-style) for all text files
- **Encoding**: UTF-8 for all text files
- **Prefer explicit types** over `var` (except when type is apparent)
- **Use language keywords** instead of framework type names (e.g., `string` not `String`)
- **Prefer modern C# features**: pattern matching, null propagation, expression-bodied members

### Best Practices

- **Dependency Injection**: All services should be registered in `StartupExtensions.cs`
- **Fail-fast validation**: Validate configuration at startup
- **Resilience patterns**: Use standard HTTP resilience with exponential backoff
- **No inline comments** unless necessary to explain complex logic (match existing style)
- **Use existing libraries** - avoid adding new dependencies unless necessary
- **Never commit secrets** - all credentials via environment variables

### Security Guidelines

- **Input validation**: Always validate and sanitize user input
- **Authentication**: Use ASP.NET Core Identity patterns
- **Secrets management**: Use environment variables, never hardcode credentials
- **Dependencies**: Keep dependencies updated (Dependabot enabled). Note: You'll need the most up-to-date .NET version matching your lockfile as CI uses the latest version.
- **Error handling**: Never expose sensitive information in error messages

See [.editorconfig](.editorconfig) for complete style rules and the [Copilot Instructions](.github/copilot-instructions.md) for detailed architectural patterns.

## Testing Guidelines

All code changes should include appropriate tests. BridgeBeats uses MSTest with Moq and FluentAssertions.

### Test Organization

- **Unit Tests**: `Tests/Unit/` - Test individual components in isolation
- **Integration Tests**: `Tests/Integration/` - Test service interactions
- **End-to-End Tests**: `Tests/EndToEnd/` - Test full request/response flows

### Writing Tests

- Use **MSTest** attributes: `[TestClass]`, `[TestMethod]`, `[TestInitialize]`, `[TestCleanup]`
- Use **Moq** for mocking dependencies
- Use **FluentAssertions** for readable assertions (e.g., `result.Should().NotBeNull()`)
- Use **WebApplicationFactory** for integration testing web endpoints
- Test both success and failure scenarios
- Name tests clearly to describe what they test

### Test Requirements

- **Bug fixes**: Add tests that reproduce the bug and verify the fix
- **New features**: Add comprehensive tests covering main scenarios and edge cases
- **Breaking changes**: Update existing tests as needed
- **All tests must pass**: PRs cannot be merged with failing tests

### Running Tests Locally

```bash
# Build first
dotnet build

# Run all tests
dotnet test

# Run specific test category
dotnet test --filter "FullyQualifiedName~Unit"
dotnet test --filter "FullyQualifiedName~Integration"
dotnet test --filter "FullyQualifiedName~EndToEnd"

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"
```

## Documentation Guidelines

Clear documentation helps everyone use and contribute to BridgeBeats.

### What to Document

- **Code changes**: Update relevant documentation when changing functionality
- **New features**: Add documentation explaining how to use the feature
- **Configuration**: Document new environment variables or settings
- **API changes**: Update the [API Reference](docs/API.md)
- **Breaking changes**: Clearly document in PR and update migration guides

### Documentation Standards

- **Clear and concise**: Write for your audience (users vs. developers)
- **Include examples**: Show practical usage with code samples
- **Keep it updated**: Documentation should match the current codebase
- **Use proper formatting**: Follow Markdown conventions
- **Link related docs**: Help users discover related information

### Documentation Structure

- **README.md**: Project overview, quick start, and links
- **docs/**: Detailed guides for specific topics
- **Code comments**: Explain complex logic (sparingly, match existing style)
- **API documentation**: Generated via DocFX from XML comments

### Submitting Documentation Changes

- Small fixes (typos, broken links): Open a PR directly
- Larger changes: Open a [documentation issue](https://github.com/tsmarvin/BridgeBeats/issues/new?template=documentation.yml) first to discuss

## Additional Resources

- **[Code of Conduct](CODE_OF_CONDUCT.md)** - Community standards
- **[Security Policy](SECURITY.md)** - Security reporting and response
- **[License](LICENSE)** - MIT License terms
- **[.editorconfig](.editorconfig)** - Code style rules
- **[Issue Templates](.github/ISSUE_TEMPLATE/)** - Bug reports, features, docs
- **[Pull Request Template](.github/PULL_REQUEST_TEMPLATE.md)** - PR guidelines

## Recognition

All contributors are recognized in our release notes and commit history. Significant contributors may be highlighted in the README. We appreciate everyone who helps make BridgeBeats better!

---

**Thank you for contributing to BridgeBeats! Your efforts help make music sharing universal.** 🎵

*Because music connects us—no matter where we listen.*
