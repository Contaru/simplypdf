# Adobe Core 14 AFM files

Font metrics for the standard 14 PDF fonts (the Latin-text subset used by SimplyPdf:
Helvetica, Times and Courier families). They are only consumed at development time by
`tools/AfmToCSharp`, which turns them into `src/SimplyPdf/Fonts/StandardFontMetrics.Data.g.cs`.

Regenerate after touching the generator:

```sh
dotnet run --project tools/AfmToCSharp -- tools/afm src/SimplyPdf/Fonts/StandardFontMetrics.Data.g.cs
```

## License

Copyright (c) 1985-1997 Adobe Systems Incorporated. All Rights Reserved.

Adobe permits these AFM files to be used, copied and distributed for any purpose and
without charge, provided that all copyright notices are retained. Helvetica and Times are
trademarks of Linotype-Hell AG and/or its subsidiaries.
