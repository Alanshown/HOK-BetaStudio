# Studio-HoK reader source

This directory contains the C# reader/utility/PInvoke/FBX-wrapper source used by HOK BetaStudio, copied from the supplied Studio-HoK source tree. The original MIT notice is preserved in `LICENSE`.

No compiled native libraries or private key database is included. `Keys.example.json` is an empty default; provide a local `Keys.json` only if you have authorized keys for a format that requires them. Keys are excluded from Git.

`Hok.Legacy` excludes upstream `AssetsManager.cs` and `EndianBinaryReader.cs` in favor of the project's maintained adapters. The upstream source snapshot is retained separately to make that boundary explicit.
