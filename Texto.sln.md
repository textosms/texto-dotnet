# Repository layout

```text
src/Texto/           the Texto.Sdk package
tests/Texto.Tests/   xUnit tests
```

Build and test:

```bash
dotnet test
dotnet pack src/Texto/Texto.csproj -c Release -o artifacts
```
