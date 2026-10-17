# Contributing to Hangfire.Dashboard.JobSearch

Thank you for your interest in contributing to **Hangfire.Dashboard.JobSearch**! We welcome bug reports, feature requests, documentation improvements, and pull requests.

---

## 1. Code of Conduct

This project adheres to the Contributor Covenant [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code.

---

## 2. Development Setup

### Prerequisites
- [.NET SDK 8.0+](https://dotnet.microsoft.com/download)
- Optional for Redis testing: Docker or a local Redis instance (`localhost:6379`)
- Optional for running example jobs: Python 3.8+

### Clone & Build
```bash
git clone https://github.com/fksegundo/Hangfire.Dashboard.JobSearch.git
cd Hangfire.Dashboard.JobSearch
dotnet build Hangfire.Dashboard.JobSearch.slnx
```

### Running Tests
```bash
dotnet test Hangfire.Dashboard.JobSearch.slnx
```

*Note: Redis storage tests use [Testcontainers.Redis](https://dotnet.testcontainers.org/) to spin up ephemeral test instances automatically if Docker is running.*

---

## 3. Submitting Pull Requests

1. **Fork** the repository and create your feature branch:
   ```bash
   git checkout -b feat/your-feature-name
   ```
2. **Commit** your changes with descriptive commit messages following Conventional Commits (e.g. `feat: ...`, `fix: ...`, `docs: ...`).
3. Ensure unit and integration tests pass.
4. Add relevant unit tests for new functionality.
5. Push to your fork and submit a **Pull Request** against `main`.

---

## 4. Reporting Issues

Please open an issue on GitHub and provide:
- A clear, concise title and description.
- Reproduction steps or minimal code sample.
- Environment details (.NET version, Hangfire version, storage backend used).
