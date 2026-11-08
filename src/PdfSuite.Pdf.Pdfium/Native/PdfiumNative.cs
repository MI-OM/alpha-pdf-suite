using System.Runtime.InteropServices;

namespace PdfSuite.Pdf.Pdfium.Native;

// Project-owned bindings for the subset of the PDFium C API that the spike uses.
// Signatures follow public/*.h at chromium/8111 (bblanchon.PDFium 158.0.8111).
// PDFium's "unsigned long" (including FPDF_DWORD) is 32-bit on Windows and 64-bit on
// Linux and macOS, so it is bound as CULong. FPDF_BOOL results are bound as int.
// None of these functions are thread-safe; callers hold PdfiumLibrary.Sync.
internal static unsafe partial class PdfiumNative
{
    private const string Library = "pdfium";

    // FPDF_GetLastError codes.
    internal const uint FPDF_ERR_FILE = 2;
    internal const uint FPDF_ERR_FORMAT = 3;
    internal const uint FPDF_ERR_PASSWORD = 4;
    internal const uint FPDF_ERR_SECURITY = 5;

    // FPDF_SaveAsCopy flags.
    internal const uint FPDF_INCREMENTAL = 1;
    internal const uint FPDF_NO_INCREMENTAL = 2;

    // FPDF_RenderPageBitmap / FPDF_RenderPage flags.
    internal const int FPDF_ANNOT = 0x01;
    internal const int FPDF_PRINTING = 0x800;

    internal const int FPDF_ANNOT_TEXT = 1;
    internal const int FPDF_FILLMODE_ALTERNATE = 1;

    // fpdfview.h
    [LibraryImport(Library)]
    internal static partial void FPDF_InitLibrary();

    [LibraryImport(Library)]
    internal static partial CULong FPDF_GetLastError();

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint FPDF_LoadMemDocument64(void* dataBuffer, nuint size, string? password);

    [LibraryImport(Library)]
    internal static partial void FPDF_CloseDocument(nint document);

    [LibraryImport(Library)]
    internal static partial int FPDF_GetPageCount(nint document);

    // Writes the value as UTF-16LE with a terminator; returns the byte count needed.
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial CULong FPDF_GetMetaText(nint document, string tag, void* buffer, CULong bufferLength);

    // The version as 10 times its value (14 for PDF 1.4). Returns false if it is unknown.
    [LibraryImport(Library)]
    internal static partial int FPDF_GetFileVersion(nint document, int* fileVersion);

    // The access permissions this document was opened with: all bits for an unencrypted document,
    // or one opened with its owner password.
    [LibraryImport(Library)]
    internal static partial CULong FPDF_GetDocPermissions(nint document);

    // The standard security handler's revision, or -1 if the document is not encrypted.
    [LibraryImport(Library)]
    internal static partial int FPDF_GetSecurityHandlerRevision(nint document);

    // fpdf_catalog.h
    [LibraryImport(Library)]
    internal static partial int FPDFCatalog_IsTagged(nint document);

    [LibraryImport(Library)]
    internal static partial int FPDF_GetPageSizeByIndexF(nint document, int pageIndex, FS_SIZEF* size);

    [LibraryImport(Library)]
    internal static partial nint FPDF_LoadPage(nint document, int pageIndex);

    [LibraryImport(Library)]
    internal static partial void FPDF_ClosePage(nint page);

    // Windows builds only: draws onto a GDI device context such as a printer's. Text is drawn as
    // glyph outlines: FPDF_SetPrintTextWithGDI, which sends GDI text instead, is not exported by these
    // builds because it needs PDFIUM_PRINT_TEXT_WITH_GDI at build time (spike results, S3).
    [LibraryImport(Library)]
    internal static partial void FPDF_RenderPage(
        nint deviceContext, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [LibraryImport(Library)]
    internal static partial void FPDF_RenderPageBitmap(
        nint bitmap, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [LibraryImport(Library)]
    internal static partial nint FPDFBitmap_Create(int width, int height, int alpha);

    internal const int FPDFBitmap_BGRA = 4;

    // Wraps a caller-owned buffer; FPDFBitmap_Destroy does not free it.
    [LibraryImport(Library)]
    internal static partial nint FPDFBitmap_CreateEx(int width, int height, int format, void* firstScan, int stride);

    // Returns FPDF_BOOL in newer headers and void in older ones; the result is not needed.
    [LibraryImport(Library)]
    internal static partial void FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, CULong color);

    [LibraryImport(Library)]
    internal static partial void* FPDFBitmap_GetBuffer(nint bitmap);

    [LibraryImport(Library)]
    internal static partial int FPDFBitmap_GetStride(nint bitmap);

    [LibraryImport(Library)]
    internal static partial void FPDFBitmap_Destroy(nint bitmap);

    // fpdf_edit.h
    [LibraryImport(Library)]
    internal static partial nint FPDF_CreateNewDocument();

    [LibraryImport(Library)]
    internal static partial nint FPDFPage_New(nint document, int pageIndex, double width, double height);

    [LibraryImport(Library)]
    internal static partial void FPDFPage_InsertObject(nint page, nint pageObject);

    [LibraryImport(Library)]
    internal static partial int FPDFPage_RemoveObject(nint page, nint pageObject);

    [LibraryImport(Library)]
    internal static partial int FPDFPage_CountObjects(nint page);

    [LibraryImport(Library)]
    internal static partial nint FPDFPage_GetObject(nint page, int index);

    [LibraryImport(Library)]
    internal static partial int FPDFPage_GenerateContent(nint page);

    [LibraryImport(Library)]
    internal static partial void FPDFPageObj_Destroy(nint pageObject);

    [LibraryImport(Library)]
    internal static partial int FPDFPageObj_GetType(nint pageObject);

    [LibraryImport(Library)]
    internal static partial void FPDFPageObj_Transform(
        nint pageObject, double a, double b, double c, double d, double e, double f);

    [LibraryImport(Library)]
    internal static partial int FPDFPageObj_SetFillColor(nint pageObject, uint r, uint g, uint b, uint a);

    [LibraryImport(Library)]
    internal static partial nint FPDFPageObj_CreateNewRect(float x, float y, float width, float height);

    [LibraryImport(Library)]
    internal static partial int FPDFPath_SetDrawMode(nint path, int fillMode, int stroke);

    [LibraryImport(Library)]
    internal static partial nint FPDFPageObj_NewImageObj(nint document);

    [LibraryImport(Library)]
    internal static partial int FPDFImageObj_SetBitmap(nint* pages, int count, nint imageObject, nint bitmap);

    [LibraryImport(Library)]
    internal static partial int FPDFImageObj_SetMatrix(
        nint imageObject, double a, double b, double c, double d, double e, double f);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint FPDFText_LoadStandardFont(nint document, string font);

    [LibraryImport(Library)]
    internal static partial void FPDFFont_Close(nint font);

    [LibraryImport(Library)]
    internal static partial nint FPDFPageObj_CreateTextObj(nint document, nint font, float fontSize);

    [LibraryImport(Library)]
    internal static partial int FPDFText_SetText(nint textObject, char* text);

    // fpdf_ppo.h
    [LibraryImport(Library)]
    internal static partial nint FPDF_NewXObjectFromPage(nint destDocument, nint sourceDocument, int sourcePageIndex);

    [LibraryImport(Library)]
    internal static partial void FPDF_CloseXObject(nint xobject);

    [LibraryImport(Library)]
    internal static partial nint FPDF_NewFormObjectFromXObject(nint xobject);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int FPDF_ImportPages(nint destDocument, nint sourceDocument, string? pageRange, int index);

    // fpdf_text.h
    [LibraryImport(Library)]
    internal static partial nint FPDFText_LoadPage(nint page);

    [LibraryImport(Library)]
    internal static partial void FPDFText_ClosePage(nint textPage);

    [LibraryImport(Library)]
    internal static partial int FPDFText_CountChars(nint textPage);

    [LibraryImport(Library)]
    internal static partial int FPDFText_GetText(nint textPage, int startIndex, int count, char* result);

    [LibraryImport(Library)]
    internal static partial uint FPDFText_GetUnicode(nint textPage, int index);

    // Note the argument order: left, right, bottom, top (PDF space, origin bottom-left).
    [LibraryImport(Library)]
    internal static partial int FPDFText_GetCharBox(nint textPage, int index, double* left, double* right, double* bottom, double* top);

    // The character's box with the font's full ascent and descent, so every character on a line
    // has the same height. Page coordinates, origin bottom-left.
    [LibraryImport(Library)]
    internal static partial int FPDFText_GetLooseCharBox(nint textPage, int index, FS_RECTF* rect);

    [LibraryImport(Library)]
    internal static partial int FPDFText_CountRects(nint textPage, int startIndex, int count);

    [LibraryImport(Library)]
    internal static partial int FPDFText_GetRect(nint textPage, int rectIndex, double* left, double* top, double* right, double* bottom);

    // fpdf_annot.h
    [LibraryImport(Library)]
    internal static partial nint FPDFPage_CreateAnnot(nint page, int subtype);

    [LibraryImport(Library)]
    internal static partial int FPDFPage_GetAnnotCount(nint page);

    [LibraryImport(Library)]
    internal static partial void FPDFPage_CloseAnnot(nint annotation);

    [LibraryImport(Library)]
    internal static partial int FPDFAnnot_SetRect(nint annotation, FS_RECTF* rect);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int FPDFAnnot_SetStringValue(nint annotation, string key, char* value);

    // fpdf_attachment.h
    [LibraryImport(Library)]
    internal static partial int FPDFDoc_GetAttachmentCount(nint document);

    [LibraryImport(Library)]
    internal static partial nint FPDFDoc_AddAttachment(nint document, char* name);

    [LibraryImport(Library)]
    internal static partial int FPDFAttachment_SetFile(nint attachment, nint document, void* contents, CULong length);

    // fpdf_save.h
    [LibraryImport(Library)]
    internal static partial int FPDF_SaveAsCopy(nint document, FPDF_FILEWRITE* fileWrite, CULong flags);

    // Text objects and fonts, for the glyph-level redaction probe (spike S5). PDFium can set a
    // text object's character codes but has no call to read them.
    [LibraryImport(Library)]
    internal static partial nint FPDFText_GetTextObject(nint textPage, int index);

    // Content marks: "/Tag <properties> BDC ... EMC" in a content stream. Names and string values
    // come back as UTF-16LE with a terminating NUL; the length out-parameters are in bytes.
    [LibraryImport(Library)]
    internal static partial int FPDFPageObj_CountMarks(nint pageObject);

    [LibraryImport(Library)]
    internal static partial nint FPDFPageObj_GetMark(nint pageObject, CULong index);

    [LibraryImport(Library)]
    internal static partial int FPDFPageObjMark_GetName(nint mark, char* buffer, CULong bufferLength, CULong* outLength);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int FPDFPageObjMark_GetParamStringValue(nint mark, string key, char* buffer, CULong bufferLength, CULong* outLength);

    [LibraryImport(Library)]
    internal static partial int FPDFPageObj_GetMarkedContentID(nint pageObject);

    // Text search. The query is NUL-terminated UTF-16. Flags: FPDF_MATCHCASE, FPDF_MATCHWHOLEWORD.
    internal const uint FPDF_MATCHCASE = 0x1;
    internal const uint FPDF_MATCHWHOLEWORD = 0x2;

    [LibraryImport(Library)]
    internal static partial nint FPDFText_FindStart(nint textPage, char* query, CULong flags, int startIndex);

    [LibraryImport(Library)]
    internal static partial int FPDFText_FindNext(nint search);

    [LibraryImport(Library)]
    internal static partial int FPDFText_GetSchResultIndex(nint search);

    [LibraryImport(Library)]
    internal static partial int FPDFText_GetSchCount(nint search);

    [LibraryImport(Library)]
    internal static partial void FPDFText_FindClose(nint search);

    // Structure tree (tagged PDF). A child that is marked content rather than an element comes
    // back from GetChildAtIndex as null, and its ID from GetChildMarkedContentID.
    [LibraryImport(Library)]
    internal static partial nint FPDF_StructTree_GetForPage(nint page);

    [LibraryImport(Library)]
    internal static partial void FPDF_StructTree_Close(nint structTree);

    [LibraryImport(Library)]
    internal static partial int FPDF_StructTree_CountChildren(nint structTree);

    [LibraryImport(Library)]
    internal static partial nint FPDF_StructTree_GetChildAtIndex(nint structTree, int index);

    [LibraryImport(Library)]
    internal static partial int FPDF_StructElement_CountChildren(nint structElement);

    [LibraryImport(Library)]
    internal static partial nint FPDF_StructElement_GetChildAtIndex(nint structElement, int index);

    [LibraryImport(Library)]
    internal static partial int FPDF_StructElement_GetChildMarkedContentID(nint structElement, int index);

    [LibraryImport(Library)]
    internal static partial int FPDFText_GetCharOrigin(nint textPage, int index, double* x, double* y);

    [LibraryImport(Library)]
    internal static partial nint FPDFTextObj_GetFont(nint textObject);

    [LibraryImport(Library)]
    internal static partial int FPDFTextObj_GetFontSize(nint textObject, float* size);

    [LibraryImport(Library)]
    internal static partial int FPDFPageObj_GetMatrix(nint pageObject, FS_MATRIX* matrix);

    [LibraryImport(Library)]
    internal static partial int FPDFPageObj_SetMatrix(nint pageObject, FS_MATRIX* matrix);

    [LibraryImport(Library)]
    internal static partial int FPDFFont_GetIsEmbedded(nint font);

    // Returns the name's length including the terminating NUL; writes only if the buffer is long enough.
    [LibraryImport(Library)]
    internal static partial nuint FPDFFont_GetBaseFontName(nint font, byte* buffer, nuint length);

    internal const int FPDF_FONT_TRUETYPE = 2;

    [LibraryImport(Library)]
    internal static partial nint FPDFText_LoadFont(nint document, byte* data, uint size, int fontType, int cid);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FS_MATRIX
{
    public float A;
    public float B;
    public float C;
    public float D;
    public float E;
    public float F;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FS_SIZEF
{
    public float Width;
    public float Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FS_RECTF
{
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;
}

// PDFium's FPDF_FILEWRITE, followed by one field of our own. PDFium passes this struct's
// address back to WriteBlock, which uses State to find the managed save state.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FPDF_FILEWRITE
{
    public int Version;
    public delegate* unmanaged[Cdecl]<FPDF_FILEWRITE*, void*, CULong, int> WriteBlock;
    public nint State;
}
