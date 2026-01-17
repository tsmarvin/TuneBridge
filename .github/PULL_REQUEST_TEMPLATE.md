## Description

<!-- Provide a brief summary of your changes and the motivation behind them -->

Fixes #(issue)

## Type of Change

<!-- Check all that apply -->

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Configuration change
- [ ] Performance improvement
- [ ] Code refactoring
- [ ] Dependency update

## Testing Checklist

<!-- Verify that appropriate tests have been added/updated -->

- [ ] Unit tests added/updated (run with `dotnet test --filter "FullyQualifiedName~Unit"`)
- [ ] Integration tests added/updated (run with `dotnet test --filter "FullyQualifiedName~Integration"`)
- [ ] End-to-end tests added/updated (run with `dotnet test --filter "FullyQualifiedName~EndToEnd"`)
- [ ] All tests pass locally (`dotnet test`)
- [ ] Manual testing completed (if applicable)

## Code Quality Checklist

<!-- Verify that code meets quality standards -->

- [ ] Code follows the style guidelines from `.editorconfig`
- [ ] Code builds without warnings (`dotnet build --configuration Release`)
- [ ] No new compiler warnings or errors introduced
- [ ] Self-review of code completed
- [ ] Code comments added where necessary (especially for complex logic)
- [ ] No unnecessary comments or debug code left in

## Documentation Checklist

<!-- Verify that documentation is updated -->

- [ ] README.md updated (if user-facing changes)
- [ ] API.md updated (if API changes)
- [ ] Configuration.md updated (if configuration changes)
- [ ] Code documentation (XML comments) added for public APIs
- [ ] DocFX documentation updated (if applicable)
- [ ] Environment variable changes documented

## Security Checklist

<!-- Verify security considerations have been addressed -->

- [ ] No secrets, API keys, or credentials committed
- [ ] Input validation added for new user inputs
- [ ] SQL injection risks considered (if applicable)
- [ ] XSS risks considered (if applicable)
- [ ] Authentication/authorization implemented correctly (if applicable)
- [ ] Dependencies checked for known vulnerabilities
- [ ] Changes reviewed against SECURITY.md guidelines

## Deployment Checklist

<!-- Verify deployment considerations -->

- [ ] Docker build tested (`docker build -t bridgebeats .`)
- [ ] Environment variables documented in `.env.example` (if new variables added)
- [ ] Database migrations included (if applicable)
- [ ] Breaking changes documented in PR description
- [ ] Backward compatibility maintained (or breaking changes clearly documented)

## Additional Notes

<!-- Add any additional context, screenshots, or information that reviewers should know -->

### Screenshots (if applicable)

<!-- Add screenshots for UI changes -->

### Breaking Changes (if applicable)

<!-- Detail any breaking changes and migration steps -->

### Related Issues/PRs

<!-- Link to related issues or pull requests -->

## Reviewer Guidance

<!-- Help reviewers understand what to focus on -->

### Areas to Focus On

<!-- List specific areas that need careful review -->

### Testing Instructions

<!-- Provide step-by-step instructions for testing your changes -->

1. 
2. 
3. 

## Pre-Submission Checklist

<!-- Final checks before submitting -->

- [ ] PR title is clear and descriptive
- [ ] PR is linked to related issue(s)
- [ ] All CI checks pass (or failures are explained)
- [ ] Code is ready for review
- [ ] Changes are minimal and focused on the issue

---

**By submitting this pull request, I confirm that my contribution is made under the terms of the MIT License.**
