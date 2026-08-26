# Dependencias de terceros

## Tesseract para .NET 5.2.0

- Proyecto: `charlesw/tesseract`.
- Uso: wrapper .NET y binarios privados de Tesseract/Leptonica para OCR local.
- Licencia declarada por el paquete NuGet: Apache-2.0.
- Fuente: https://github.com/charlesw/tesseract
- Paquete: https://www.nuget.org/packages/Tesseract/5.2.0

El paquete despliega `Tesseract.dll`, `tesseract50.dll` y `leptonica-1.82.0.dll` para x86 y x64. Requiere los runtimes de Visual C++ 2019 correspondientes a la arquitectura del proceso.

## Modelo español tessdata_fast

- Archivo desplegado: `App_Data/Tessdata/spa.traineddata`.
- Proyecto oficial: `tesseract-ocr/tessdata_fast`.
- Commit de origen: `87416418657359cb625c412a48b6e1d6d41c29bd`.
- URL fijada: https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/87416418657359cb625c412a48b6e1d6d41c29bd/spa.traineddata
- SHA-256: `6F2E04D02774A18F01BED44B1111F2CD7F3BA7AC9DC4373CD3F898A40EA6B464`.
- Licencia: Apache-2.0; el texto redistribuido está en `App_Data/Tessdata/LICENSE-tessdata_fast.txt`.

Se eligió `tessdata_fast` por su equilibrio entre precisión y velocidad. El modelo usa el motor LSTM; no se incorporaron simultáneamente modelos `tessdata` ni `tessdata_best`.

## PDFtoImage 5.4.0

- Proyecto: `sungaila/PDFtoImage`.
- Uso: rasterización local de páginas PDF como fallback acotado para OCR.
- Licencia declarada por el paquete NuGet: MIT.
- Fuente: https://github.com/sungaila/PDFtoImage
- Paquete: https://www.nuget.org/packages/PDFtoImage/5.4.0

## SkiaSharp 4.150.1 y SkiaSharp.NativeAssets.Win32 4.150.1

- Proyecto: SkiaSharp, Microsoft.
- Uso: superficie gráfica administrada y runtime nativo Win32 requeridos por PDFtoImage.
- Licencia declarada por ambos paquetes NuGet: MIT.
- Fuente: https://github.com/mono/SkiaSharp
- Paquetes: https://www.nuget.org/packages/SkiaSharp/4.150.1 y https://www.nuget.org/packages/SkiaSharp.NativeAssets.Win32/4.150.1

## bblanchon.PDFium.Win32 152.0.7961

- Proyecto: `bblanchon/pdfium-binaries`.
- Uso: binario PDFium x64 requerido por PDFtoImage en Windows.
- Licencia declarada por el paquete NuGet: Apache-2.0.
- Fuente: https://github.com/bblanchon/pdfium-binaries
- Paquete: https://www.nuget.org/packages/bblanchon.PDFium.Win32/152.0.7961
