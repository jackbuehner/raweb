This is the installer for RAWeb.

This installer uses .NET SDK version 9. Download the SDK from https://dotnet.microsoft.com/en-us/download/dotnet/9.0.

To build a test version of the installer, run `dotnet build`

To build a self-contained exe version of the installer, run:

```
dotnet publish -c Release
```

Note: If you omit "-c Release", it will generate a debug build, which lacks optimizations.
