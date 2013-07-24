
MKDIR ChmProcessor\bin
MKDIR ChmProcessor\bin\x86
MKDIR ChmProcessor\bin\x86\Debug

COPY doc\chmProcessor.chm ChmProcessor\bin\x86\Debug
COPY license.txt ChmProcessor\bin\x86\Debug
MKDIR ChmProcessor\bin\x86\Debug\webFiles
COPY webFiles ChmProcessor\bin\x86\Debug\webFiles
MKDIR ChmProcessor\bin\x86\Debug\webTranslations
COPY webTranslations ChmProcessor\bin\x86\Debug\webTranslations
copy ChmProcessor\dialog-information.png ChmProcessor\bin\x86\Debug
copy ChmProcessor\dialog-error.png ChmProcessor\bin\x86\Debug
copy searchdb.sql ChmProcessor\bin\x86\Debug
copy doc\web\chmProcessorDocumentation.pdf ChmProcessor\bin\x86\Debug
copy libraries\tidy.exe ChmProcessor\bin\x86\Debug

REM PREPARE SEARCH FILES:
MKDIR ChmProcessor\bin\x86\Debug\searchFiles
MKDIR ChmProcessor\bin\x86\Debug\searchFiles\Bin
COPY WebFullTextSearch\Bin ChmProcessor\bin\x86\Debug\searchFiles\Bin
COPY WebFullTextSearch\search.aspx ChmProcessor\bin\x86\Debug\searchFiles
COPY WebFullTextSearch\search.aspx.cs ChmProcessor\bin\x86\Debug\searchFiles
COPY WebFullTextSearch\Web.Config ChmProcessor\bin\x86\Debug\searchFiles

PAUSE
