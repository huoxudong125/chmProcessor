/* 
 * chmProcessor - Word converter to CHM
 * Copyright (C) 2008 Toni Bennasar Obrador
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using mshtml;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Management;
using System.Web;
using System.Runtime.InteropServices;
using System.Globalization;
using WebIndexLib;


namespace ChmProcessorLib
{

	/// <summary>
    /// Class to handle manipulations of a HTML / Word file to generate the help
	/// </summary>
    public class DocumentProcessor
    {

        /// <summary>
        /// Name for the help project that will be generated
        /// </summary>
        public static string NOMBREPROYECTO = "help.hhp";

        /// <summary>
        /// Name for the chm file that will be generated (?)
        /// </summary>
        public static string NOMBREARCHIVOAYUDA = "help.chm";

        /// <summary>
        /// Main source file to convert to help.
        /// If there was multiple documents to convert to help, they are joined on this.
        /// </summary>
        private string MainSourceFile;

        /// <summary>
        /// Documento HTML cargado:
        /// </summary>
        private IHTMLDocument2 iDoc;

        /// <summary>
        /// HTML title nodes (H1,H2,etc) tree.
        /// </summary>
        private ArbolCapitulos tree;

        /// <summary>
        /// Decorator for pages for the generated CHM 
        /// </summary>
        private HtmlPageDecorator chmDecorator = new HtmlPageDecorator();

        /// <summary>
        /// Decorator for pages for the generated web site and the JavaHelp file.
        /// </summary>
        private HtmlPageDecorator webDecorator = new HtmlPageDecorator();

        /// <summary>
        /// Lista de archivos y directorios adicionales a a�adir al proyecto de ayuda
        /// </summary>
        private ArrayList ArchivosAdicionales;

        /// <summary>
        /// Indica si el archivo a procesar es un documento word o uno html
        /// </summary>
        private bool esWord;

        /// <summary>
        /// Si esWord = true, indica el directorio temporal donde se genero el 
        /// html del documento word
        /// </summary>
        private string dirHtml;

        /// <summary>
        /// HTML content of the body of the first chapter into the document.
        /// </summary>
        private string FirstChapterContent;

        /// <summary>
        /// Project to generate the help. 
        /// </summary>
        public ChmProject Project;

        /// <summary>
        /// List of exceptions catched on the generation process.
        /// </summary>
        public ArrayList GenerationExceptions = new ArrayList();

        /// <summary>
        /// Timer to avoid html loading hang ups
        /// </summary>
        private System.Windows.Forms.Timer timerTimeout;

        /// <summary>
        /// Handler of the user interface of the generation process. Can be null.
        /// </summary>
        public UserInterface UI;

        /// <summary>
        /// Encoding to write the help workshop project files.
        /// </summary>
        private Encoding helpWorkshopEncoding;

        /// <summary>
        /// Culture to put into the help workshop project file.
        /// </summary>
        private CultureInfo helpWorkshopCulture;

        /// <summary>
        /// Should we replace / remove broken links?
        /// It gets its value from <see cref="AppSettings.ReplaceBrokenLinks"/>
        /// </summary>
        private bool replaceBrokenLinks;

        private void log(string texto, int logLevel) 
        {
            if (UI != null)
                UI.log(texto, logLevel);
        }

        /// <summary>
        /// Stores an exception into the log.
        /// </summary>
        /// <param name="exception">Exception to log</param>
        private void log(Exception exception)
        {
            GenerationExceptions.Add(exception);
            if (UI != null)
                UI.log(exception);
        }

        private bool CancellRequested()
        {
            if (UI != null)
                return UI.CancellRequested();
            else
                return false;
        }

        [ComVisible(true), ComImport(),
        Guid("7FD52380-4E07-101B-AE2D-08002B2EC713"),
        InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPersistStreamInit
        {
            void GetClassID([In, Out] ref Guid pClassID);
            [return: MarshalAs(UnmanagedType.I4)]
            [PreserveSig]
            int IsDirty();
            [return: MarshalAs(UnmanagedType.I4)]
            [PreserveSig]
            int Load([In] UCOMIStream pstm);
            [return: MarshalAs(UnmanagedType.I4)]
            [PreserveSig]
            int Save([In] UCOMIStream pstm, [In,
                MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
            void GetSizeMax([Out] long pcbSize);
            [return: MarshalAs(UnmanagedType.I4)]
            [PreserveSig]
            int InitNew();
        }

        /// <summary>
        /// Timer to avoid load html file hang up
        /// </summary>
        private void timer_Tick(object sender, System.EventArgs e)
        {
            timerTimeout.Enabled = false;
        }

        /// <summary>
        /// Checks if any source document is open, and join the multiple source Word documents to 
        /// a single file.
        /// </summary>
        /// <param name="msWord">Word instace that will do this work.</param>
        /// <returns>Path to a single Word file containing all source documents. If there is a 
        /// single source file, it will return that file path.</returns>
        private string CheckAndJoinWordSourceFiles(MSWord msWord)
        {

            // Check any source file is open:
            foreach (string sourceFile in Project.SourceFiles)
            {
                if (msWord.IsOpen(sourceFile))
                    throw new Exception("The file " + sourceFile + " is already open. You must to close it before generate the help.");
            }

            if (Project.SourceFiles.Count == 1)
                return (string) Project.SourceFiles[0];

            // Join multiple documents to a temporal file:
            string joinedDocument = Path.GetTempFileName();
            // Add DOC extension:
            joinedDocument += ".doc";

            log("Joining documents to a single temporal file : " + joinedDocument, ConsoleUserInterface.INFO);
            msWord.JoinDocuments(Project.SourceFiles.ToArray(), joinedDocument);
            return joinedDocument;
        }

        /// <summary>
        /// Open source files, if they are MS Word documents.
        /// It joins and store them to a HTML single file.
        /// </summary>
        /// <param name="msWord">Word instace that will do this work.</param>
        /// <returns>The HTML joined version of the MS Word documents</returns>
        private string ConvertWordSourceFiles(MSWord msWord)
        {

            MainSourceFile = CheckAndJoinWordSourceFiles(msWord);

            if (CancellRequested())
                return null;

            log("Convert file " + MainSourceFile + " to HTML", ConsoleUserInterface.INFO);
            string nombreArchivo = Path.GetFileNameWithoutExtension(MainSourceFile);
            dirHtml = Path.GetTempPath() + Path.DirectorySeparatorChar + nombreArchivo;
            if (Directory.Exists(dirHtml))
                Directory.Delete(dirHtml, true);
            else if (File.Exists(dirHtml))
                File.Delete(dirHtml);
            Directory.CreateDirectory(dirHtml);

            // Rename the file to a save name. If there is spaces, for example, 
            // links to embedded images into the document are not found.
            //string finalFile = dirHtml + Path.DirectorySeparatorChar + nombreArchivo + ".htm";
            string finalFile = dirHtml + Path.DirectorySeparatorChar + NodoArbol.ToSafeFilename(nombreArchivo) + ".htm";

            msWord.SaveWordToHtml(MainSourceFile, finalFile);
            return finalFile;
        }

        /// <summary>
        /// Open source files.
        /// If they are Word, they will be converted to HTML.
        /// </summary>
        private void OpenSourceFiles() 
        {
            MSWord msWord = null;

            try
            {
                string archivoFinal = (string)Project.SourceFiles[0];
                esWord = MSWord.ItIsWordDocument(archivoFinal);
                dirHtml = null;
                // Si es un documento word, convertirlo a HTML filtrado
                if (esWord)
                {
                    msWord = new MSWord();
                    archivoFinal = ConvertWordSourceFiles(msWord);

                    // Be sure we have closed word, to avoid overlapping between the html read
                    // and the reading from chmprocessor:
                    msWord.Dispose();
                    msWord = null;
                }
                else
                    // There is a single source HTML file.
                    MainSourceFile = (string)Project.SourceFiles[0];

                if (CancellRequested())
                    return;

                // TODO: Check if this should be removed.
                if (AppSettings.UseTidyOverInput)
                    new TidyParser(UI).Parse(archivoFinal);

                if (CancellRequested())
                    return;

                // Prepare loading:
                HTMLDocumentClass docClass = new HTMLDocumentClass();
                IPersistStreamInit ips = (IPersistStreamInit)docClass;
                ips.InitNew();

                // Create a timer, to be sure that HTML file load will not be hang up (Sometime happens)
                timerTimeout = new System.Windows.Forms.Timer();
                timerTimeout.Tick += new System.EventHandler(this.timer_Tick);
                timerTimeout.Interval = 60 * 1000;     // 1 minute
                timerTimeout.Enabled = true;

                // Load the file:
                IHTMLDocument2 docLoader = (mshtml.IHTMLDocument2)docClass.createDocumentFromUrl( archivoFinal , null);
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(1000);

                String currentStatus = docLoader.readyState;
                log("Reading file " + archivoFinal + ". Status: " + currentStatus, ConsoleUserInterface.INFO);
                while (currentStatus != "complete" && timerTimeout.Enabled)
                {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(500);
                    String newStatus = docLoader.readyState;
                    if (newStatus != currentStatus)
                    {
                        log("Status: " + newStatus, ConsoleUserInterface.INFO );
                        if (currentStatus == "interactive" && newStatus == "uninitialized")
                        {
                            // fucking shit bug. Try to reload the file:
                            log("Warning. Something wrong happens loading the file. Trying to reopen " + archivoFinal, ConsoleUserInterface.INFO);
                            docClass = new HTMLDocumentClass();
                            ips = (IPersistStreamInit)docClass;
                            ips.InitNew();
                            docLoader = (mshtml.IHTMLDocument2)docClass.createDocumentFromUrl(archivoFinal, null);
                            newStatus = docLoader.readyState;
                            log("Status: " + newStatus, ConsoleUserInterface.INFO);
                        }
                        currentStatus = newStatus;
                    }
                }
                if (!timerTimeout.Enabled)
                    log("Warning: time to load file expired.", ConsoleUserInterface.ERRORWARNING);
                timerTimeout.Enabled = false;

                // Get a copy of the document:
                // TODO: Check why is needed a copy... We cannot work with the original loaded file?
                HTMLDocumentClass newDocClass = new HTMLDocumentClass();
                iDoc = (IHTMLDocument2)newDocClass;
                object[] txtHtml = { ((IHTMLDocument3)docLoader).documentElement.outerHTML };
                iDoc.writeln(txtHtml);
                try
                {
                    // Needed, otherwise some characters will not be displayed well.
                    iDoc.charset = docLoader.charset;
                }
                catch (Exception ex)
                {
                    log("Warning: Cannot set the charset \"" + docLoader.charset + "\" to the html document. Reason:" + ex.Message, ConsoleUserInterface.ERRORWARNING);
                    log(ex);
                }
            }
            finally
            {
                if (msWord != null)
                {
                    msWord.Dispose();
                    msWord = null;
                }
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="project">Data about the document to convert to help.</param>
        public DocumentProcessor( ChmProject project )
        {
            this.Project = project;
            this.ArchivosAdicionales = new ArrayList(project.ArchivosAdicionales);
            this.replaceBrokenLinks = AppSettings.ReplaceBrokenLinks;
            try
            {
                this.helpWorkshopCulture = CultureInfo.GetCultureInfo(project.ChmLocaleID);
            }
            catch (Exception ex)
            {
                log(ex);
                throw new Exception("The locale ID (LCID) " + project.ChmLocaleID + " is not found.", ex);
            }

            try
            {
                this.helpWorkshopEncoding = Encoding.GetEncoding(helpWorkshopCulture.TextInfo.ANSICodePage);
            }
            catch (Exception ex)
            {
                log(ex);
                throw new Exception("The ANSI codepage " + helpWorkshopCulture.TextInfo.ANSICodePage + " is not found.", ex);
            }
        }

        /// <summary>
        /// Repair or remove an internal link.
        /// Given a broken internal link, it searches a section title of the document with the
        /// same text of the broken link. If its found, the destination link is modified to point to
        /// that section. If a matching section is not found, the link will be removed and its content
        /// will be keept.
        /// </summary>
        /// <param name="link">The broken link</param>
        /// <param name="parent">The parent of the broken link</param>
        private void ReplaceBrokenLink(IHTMLAnchorElement link, IHTMLElement parent)
        {
            try
            {
                // Get the text of the link
                string linkText = ((IHTMLElement)link).innerText.Trim();
                // Seach a title with the same text of the link:
                NodoArbol destinationTitle = tree.SearchBySectionTitle(linkText);
                if (destinationTitle != null)
                    // Replace the original internal broken link with this:
                    link.href = destinationTitle.Href;
                else
                {
                    // No candidate title was found. Remove the link and keep its content
                    //IHTMLElementCollection col = (IHTMLElementCollection)parent.children;
                    IHTMLElementCollection linkChildren = (IHTMLElementCollection) ((IHTMLElement)link).children;
                    IHTMLDOMNode domLink = (IHTMLDOMNode)link;
                    IHTMLDOMNode domParent = (IHTMLDOMNode)parent;
                    foreach (IHTMLElement child in linkChildren)
                        domParent.insertBefore( (IHTMLDOMNode) child, domLink);
                    domLink.removeNode(false);
                }
            }
            catch (Exception ex)
            {
                log(ex);
            }
        }

        /// <summary>
        /// Checks if the node is an internal link. 
        /// If it is, replace the destination of the link from the original source file 
        /// to the splitted file.
        /// Also checks if the link is broken. If it is, we will try to replace it.
        /// </summary>
        /// <param name="node">HTML node to verify</param>
        /// <param name="parent">Parent of the node. null if nodo has no parents.</param>
        private void PreprocessHtmlNode( IHTMLElement node , IHTMLElement parent ) 
        {
            try
            {
                if (node is IHTMLAnchorElement)
                {
                    IHTMLAnchorElement link = (IHTMLAnchorElement)node;
                    string href = link.href;
                    if (href != null)
                    {
                        // An hyperlink node

                        // Remove the about:blank
                        // TODO: Check if this is really needed.
                        href = href.Replace("about:blank", "").Replace("about:", "");

                        if (href.StartsWith("#"))
                        {
                            // A internal link.
                            // Replace it to point to the right splitted file.
                            string safeRef = NodoArbol.ToSafeFilename(href.Substring(1));
                            NodoArbol nodoArbol = tree.Raiz.BuscarEnlace(safeRef);
                            if (nodoArbol != null)
                                link.href = nodoArbol.Archivo + "#" + safeRef;
                            else
                            {
                                // Broken link.
                                log("WARNING: Broken link with text: '" + node.innerText + "'", ConsoleUserInterface.ERRORWARNING);
                                if (parent != null)
                                {
                                    String inText = parent.innerText;
                                    if (inText != null)
                                    {
                                        if (inText.Length > 200)
                                            inText = inText.Substring(0, 200) + "...";
                                        log(" near of text: '" + inText + "'", ConsoleUserInterface.ERRORWARNING);
                                    }
                                }
                                if (replaceBrokenLinks)
                                    ReplaceBrokenLink(link, parent);
                            }

                        }
                    }
                    else if (link.name != null)
                    {
                        // A HTML "boomark", the destination of a link.
                        string safeName = NodoArbol.ToSafeFilename(link.name);
                        if (!link.name.Equals(safeName))
                        {
                            // Word bug? i have found names with space characters and other bad things. 
                            // They fail into the CHM:
                            //link.name = link.name.Replace(" ", ""); < NOT WORKS
                            IHTMLDOMNode domNodeParent = (IHTMLDOMNode)node.parentElement;
                            string htmlNewNode = "<a name=" + safeName + "></a>";
                            IHTMLDOMNode newDomNode = (IHTMLDOMNode)iDoc.createElement(htmlNewNode);
                            domNodeParent.replaceChild(newDomNode, (IHTMLDOMNode)node);
                        }
                    }
                }

                IHTMLElementCollection col = (IHTMLElementCollection)node.children;
                foreach (IHTMLElement hijo in col)
                    PreprocessHtmlNode(hijo, node);
            }
            catch (Exception ex)
            {
                log(ex);
            }
        }

        private IHTMLElement BuscarNodo( IHTMLElement nodo , string tag ) 
        {
            if( nodo.tagName.ToLower().Equals( tag.ToLower() ) )
                return nodo;
            else 
            {
                IHTMLElementCollection col = (IHTMLElementCollection) nodo.children;
                foreach( IHTMLElement hijo in col ) 
                {
                    IHTMLElement encontrado = BuscarNodo( hijo , tag );
                    if( encontrado != null )
                        return encontrado;
                }
                return null;
            }
        }
        
        private void GuardarDocumentos(string directory, HtmlPageDecorator decorator, NodoArbol nodo, ArrayList archivosGenerados, WebIndex indexer) 
        {
            if( nodo.body != null ) 
            {
                string texto = "";
                if( nodo.body.innerText != null )
                    texto = nodo.body.innerText.Trim();

                if( !texto.Equals("") ) 
                {
                    bool guardar = true;
                    string titulo = "";
                    IHTMLElement seccion = null;

                    seccion = SearchFirstCutNode( nodo.body );
                    if( seccion != null && seccion.innerText != null ) 
                    {
                        titulo = seccion.innerText.Trim() ;
                        if( titulo.Length == 0 )
                            guardar = false;
                    }

                    if( guardar ) 
                    {
                        // hacer un preproceso de TODOS los nodos del cuerpo:
                        IHTMLElementCollection col = (IHTMLElementCollection)nodo.body.children;
                        foreach( IHTMLElement nodoBody in col ) 
                            PreprocessHtmlNode( nodoBody , null);

                        // Save the section, adding header, footers, etc:
                        string filePath = directory + Path.DirectorySeparatorChar + nodo.Archivo;
                        decorator.ProcessAndSavePage(nodo.body, filePath, nodo.Name);

                        if (FirstChapterContent == null)
                        {
                            // This is the first chapter of the document. Store it clean, because
                            // we will need after.
                            FirstChapterContent = nodo.body.innerHTML.Replace("about:blank", "").Replace("about:", "");
                        }

                        archivosGenerados.Add(filePath);

                        if (indexer != null)
                            // Store the document at the full text search index:
                            //indexer.AddPage(nodo.Archivo, nodo.Title, nodo.body);
                            indexer.AddPage(nodo.Archivo, nodo.Name, nodo.body);
                            
                    }
                }
            }

            foreach( NodoArbol hijo in nodo.Hijos ) 
                GuardarDocumentos( directory , decorator , hijo , archivosGenerados , indexer );
        }

        private void UnificarNodos( NodoArbol nodo ) 
        {
            try
            {
                if (nodo.Nodo != null && nodo.Nodo.innerText != null && nodo.body != null)
                {
                    // Nodo con cuerpo:

                    if (nodo.Nodo.innerText.Trim().Equals(nodo.body.innerText.Trim()) &&
                        nodo.Hijos.Count > 0)
                    {
                        // Nodo vacio y con hijos 
                        NodoArbol hijo = (NodoArbol)nodo.Hijos[0];
                        if (hijo.body != null)
                        {
                            // El hijo tiene cuerpo: Unificarlos.
                            nodo.body.insertAdjacentHTML("beforeEnd", hijo.body.innerHTML);
                            hijo.body = null;
                            //hijo.GuardadoEn(nodo.Archivo);
                            hijo.ReplaceFile(nodo.Archivo);
                        }
                    }
                }

                foreach (NodoArbol hijo in nodo.Hijos)
                    UnificarNodos(hijo);
            }
            catch (Exception ex)
            {
                log( new Exception( "There was some problem when we tried to join the empty section " +
                    nodo.Name + " with their children", ex ) );
            }
        }

        private ArrayList GuardarDocumentos(string directory, HtmlPageDecorator decorator, WebIndex indexer) 
        {
            // Intentar unificar nodos que quedarian vacios, con solo el titulo de la seccion:
            foreach( NodoArbol nodo in tree.Raiz.Hijos ) 
                UnificarNodos( nodo );

            // Recorrer el arbol en busca de nodos con cuerpo
            ArrayList archivosGenerados = new ArrayList();
            foreach (NodoArbol nodo in tree.Raiz.Hijos)
                GuardarDocumentos(directory, decorator, nodo, archivosGenerados, indexer);

            return archivosGenerados;
        }

        private void GuardarParte( IHTMLElement nuevoBody ) 
        {
            IHTMLElement sectionHeader = SearchFirstCutNode( nuevoBody );
            NodoArbol nodeToStore;
            if( sectionHeader == null )
                // If no section was found, its the first section of the document:
                nodeToStore = (NodoArbol) tree.Raiz.Hijos[0];
            else 
            {
                string aName = "";
                IHTMLAnchorElement a = BuscarNodoA( sectionHeader );
                if( a != null && a.name != null )
                    aName = NodoArbol.ToSafeFilename( a.name );
                nodeToStore = tree.Raiz.BuscarNodo( sectionHeader , aName );
            }

            if (nodeToStore == null)
            {
                string errorMessage = "Error searching node ";
                if (sectionHeader != null)
                    errorMessage += sectionHeader.innerText;
                else
                    errorMessage += "<empty>";
                Exception error = new Exception(errorMessage);
                log(error);
            }
            else
            {
                nodeToStore.body = nuevoBody;
                nodeToStore.BuildListOfContainedANames();  // Store the A name's tags of the body.
            }
        }

        /// <summary>
        /// Vacia el directorio de destino, y copia los archivos adicionales a aquel.
        /// </summary>
        /// <returns>Devuelve la lista de archivos adicionales a incluir en el proyecto de la ayuda</returns>
        private ArrayList GenerarDirDestino( string dirDst ) 
        {
            // Recrear el directorio:
            try
            {
                if (Directory.Exists(dirDst))
                    Directory.Delete(dirDst, true);
            }
            catch (Exception ex)
            {
                throw new Exception("Error deleting directory: " + dirDst + ". Is it on use?", ex);
            }

            Directory.CreateDirectory( dirDst );
            
            // Copiar los archivos adicionales
            ArrayList nuevosArchivos = new ArrayList();
            foreach( string arc in ArchivosAdicionales ) 
            //foreach (string arc in Configuration.ArchivosAdicionales) 
            {
                if( Directory.Exists( arc ) )
                {
                    // Its a directory. Copy it:
                    string dst = dirDst + Path.DirectorySeparatorChar + Path.GetFileName( arc );
                    FileSystem.CopyDirectory(arc, dst);
                }
                else if( File.Exists( arc ) ) 
                {
                    string dst = dirDst + Path.DirectorySeparatorChar + Path.GetFileName( arc );
                    File.Copy( arc , dst );
                    nuevosArchivos.Add( Path.GetFileName( arc ) );
                }
            }

            return nuevosArchivos;
        }

        /// <summary>
        /// Extract the style tag of the document to an CSS external file.
        /// </summary>
        /// <returns>Path to the generated CSS file. null if no CSS was generated</returns>
        private string CheckForStyleTags() {
            IHTMLDocument3 iDoc3 = (IHTMLDocument3) iDoc;
            IHTMLDOMChildrenCollection col = (IHTMLDOMChildrenCollection)iDoc3.childNodes;
            IHTMLHeadElement head = null;
            string cssFile = null;
            IHTMLHtmlElement html = null;
            IHTMLElement style = null;

            // Search the HTML tag:
            foreach (IHTMLElement e in col)
            {
                if (e is IHTMLHtmlElement)
                {
                    html = (IHTMLHtmlElement)e;
                    break;
                }
            }

            if (html != null)
            {
                // Search the <HEAD> tag.
                col = (IHTMLDOMChildrenCollection)((IHTMLDOMNode)html).childNodes;
                foreach (IHTMLElement e in col)
                {
                    if (e is IHTMLHeadElement)
                    {
                        head = (IHTMLHeadElement)e;
                        break;
                    }
                }
            }

            if (head != null)
            {
                // Search the first <STYLE> tag:
                col = (IHTMLDOMChildrenCollection)((IHTMLDOMNode)head).childNodes;
                foreach (IHTMLElement e in col)
                {
                    if (e is IHTMLStyleElement)
                    {
                        style = (IHTMLElement)e;
                        break;
                    }
                }
            }

            if (style != null && style.innerHTML != null )
            {
                // Remove comments
                string cssText = style.innerHTML.Replace("<!--", "").Replace("-->", "");
                // Create the CSS file:
                cssFile = Project.HelpProjectDirectory + Path.DirectorySeparatorChar + "embeddedstyles.css";
                StreamWriter writer = new StreamWriter(cssFile);
                writer.Write(cssText);
                writer.Close();
                // Replace the node by other including the CSS file.
                IHTMLDOMNode newDomNode = (IHTMLDOMNode)iDoc.createElement("<link rel=\"stylesheet\" type=\"text/css\" href=\"embeddedstyles.css\" >");
                ((IHTMLDOMNode)style).replaceNode(newDomNode);
            }

            return cssFile;
        }

        /// <summary>
        /// Generate a XPS file for the document.
        /// </summary>
        private void BuildXps()
        {
            log("Generating XPS file", ConsoleUserInterface.INFO);
            try
            {
                MSWord word = new MSWord();
                word.SaveWordToXps(MainSourceFile, Project.XpsPath);
            }
            catch (Exception ex)
            {
                log("Something wrong happened with the XPS generation. Remember you must to have Microsoft Office 2007 and the " +
                        "pdf/xps generation add-in (http://www.microsoft.com/downloads/details.aspx?FamilyID=4D951911-3E7E-4AE6-B059-A2E79ED87041&displaylang=en)", ConsoleUserInterface.ERRORWARNING);
                log(ex);
            }
        }

        private void ExecuteProjectCommandLine()
        {
            try
            {
                log("Executing '" + Project.CommandLine.Trim() + "'", ConsoleUserInterface.INFO);
                string strCmdLine = "/C " + Project.CommandLine.Trim();
                ProcessStartInfo si = new System.Diagnostics.ProcessStartInfo("CMD.exe", strCmdLine);
                si.CreateNoWindow = false;
                si.UseShellExecute = false;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                Process p = new Process();
                p.StartInfo = si;
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                log(output, ConsoleUserInterface.INFO);
                log(error, ConsoleUserInterface.ERRORWARNING);
            }
            catch (Exception ex)
            {
                log("Error executing command line ", ConsoleUserInterface.ERRORWARNING);
                log(ex);
            }
        }

        /// <summary>
        /// Handle the generated help project.
        /// It can be compiled or openened through Windows sell.
        /// </summary>
        /// <param name="helpProjectFile">Path to help file project generated</param>
        private void ProcessHelpProject(string helpProjectFile)
        {
            if (Project.Compile)
            {
                // Due to some strange bug, if we have as current drive a network drive, the generated
                // help dont show the images... So, change it to the system drive:
                string cwd = Directory.GetCurrentDirectory();
                string tempDirectory = Path.GetDirectoryName(Project.HelpProjectDirectory);
                Directory.SetCurrentDirectory(tempDirectory);
                Compile(Project.HelpFile, AppSettings.CompilerPath);
                Directory.SetCurrentDirectory(cwd);
            }
            else if (Project.OpenProject)
            {
                try
                {
                    // Abrir el proyecto de la ayuda
                    Process proceso = new Process();
                    proceso.StartInfo.FileName = helpProjectFile;
                    proceso.Start();
                }
                catch( Exception ex ) 
                {
                    log("The project " + helpProjectFile + " cannot be opened" +
                        ". Have you installed the Microsoft Help Workshop ?", ConsoleUserInterface.ERRORWARNING);
                    log(ex);
                }
            }
        }

        /// <summary>
        /// Generate a PDF file for the document.
        /// </summary>
        private void BuildPdf()
        {
            try
            {
                log("Generating PDF file", ConsoleUserInterface.INFO);
                if (Project.PdfGeneration == ChmProject.PdfGenerationWay.OfficeAddin)
                {
                    MSWord word = new MSWord();
                    word.SaveWordToPdf(MainSourceFile, Project.PdfPath);
                }
                else
                {
                    PdfPrinter pdfPrinter = new PdfPrinter();
                    pdfPrinter.ConvertToPdf(MainSourceFile, Project.PdfPath);
                }
            }
            catch (Exception ex)
            {
                if (Project.PdfGeneration == ChmProject.PdfGenerationWay.OfficeAddin)
                    log("Something wrong happened with the PDF generation. Remember you must to have Microsoft Office 2007 and the" +
                        "pdf/xps generation add-in (http://www.microsoft.com/downloads/details.aspx?FamilyID=4D951911-3E7E-4AE6-B059-A2E79ED87041&displaylang=en)", ConsoleUserInterface.ERRORWARNING);
                else
                    log("Something wrong happened with the PDF generation. Remember you must to have PdfCreator (VERSION " + PdfPrinter.SUPPORTEDVERSION + 
                        " AND ONLY THIS VERSION) installed into your computer to " +
                        "generate a PDF file. You can download it from http://www.pdfforge.org/products/pdfcreator/download", ConsoleUserInterface.ERRORWARNING);
                log(ex);
            }
        }

        public void GenerateHelp()
        {
            try
            {
                // Generate help project and java help:
                string helpProjectFile = Generate();

                if (CancellRequested())
                    return;

                // Open or compile the help project
                ProcessHelpProject(helpProjectFile);

                if (CancellRequested())
                    return;

                // PDF:
                if (Project.GeneratePdf)
                    BuildPdf();

                if (CancellRequested())
                    return;

                // XPS:
                if (Project.GenerateXps)
                    BuildXps();

                if (CancellRequested())
                    return;

                // Execute command line:
                if (Project.CommandLine != null && !Project.CommandLine.Trim().Equals(""))
                    ExecuteProjectCommandLine();
            }
            catch (Exception ex)
            {
                log("Error: " + ex.Message, ConsoleUserInterface.ERRORWARNING);
                log(ex);
                throw;
            }
        }

        /// <summary>
        /// Configure decorators to add headers, footer, metas and other stuff to the generated
        /// web pages. Call this after do any change on the original page
        /// </summary>
        private void PrepareHtmlDecorators() 
        {

            // CHM html files will use the encoding specified by the user:
            chmDecorator.ui = this.UI;
            // use the selected encoding:
            chmDecorator.OutputEncoding = helpWorkshopEncoding;

            // Web html files will be UTF-8:
            webDecorator.ui = this.UI;
            webDecorator.MetaDescriptionValue = Project.WebDescription;
            webDecorator.MetaKeywordsValue = Project.WebKeywords;
            webDecorator.OutputEncoding = Encoding.UTF8;
            webDecorator.UseTidy = true;

            if (!Project.ChmHeaderFile.Equals(""))
            {
                log("Reading chm header: " + Project.ChmHeaderFile, ConsoleUserInterface.INFO);
                chmDecorator.HeaderHtmlFile = Project.ChmHeaderFile;
            }

            if (CancellRequested())
                return;

            if (!Project.ChmFooterFile.Equals(""))
            {
                log("Reading chm footer: " + Project.ChmFooterFile, ConsoleUserInterface.INFO);
                chmDecorator.FooterHtmlFile = Project.ChmFooterFile;
            }

            if (CancellRequested())
                return;

            if (Project.GenerateWeb && !Project.WebHeaderFile.Equals(""))
            {
                log("Reading web header: " + Project.WebHeaderFile, ConsoleUserInterface.INFO);
                webDecorator.HeaderHtmlFile = Project.WebHeaderFile;
            }

            if (CancellRequested())
                return;

            if (Project.GenerateWeb && !Project.WebFooterFile.Equals(""))
            {
                log("Reading web footer: " + Project.WebFooterFile, ConsoleUserInterface.INFO);
                webDecorator.FooterHtmlFile = Project.WebFooterFile;
            }

            if (CancellRequested())
                return;

            if (Project.GenerateWeb && !Project.HeadTagFile.Equals(""))
            {
                log("Reading <header> include: " + Project.HeadTagFile, ConsoleUserInterface.INFO);
                webDecorator.HeadIncludeFile = Project.HeadTagFile;
            }

            if (CancellRequested())
                return;

            // Prepare decorators for use. Do it after extract style tags:
            webDecorator.PrepareHtmlPattern((IHTMLDocument3)iDoc);
            chmDecorator.PrepareHtmlPattern((IHTMLDocument3)iDoc);
        }

        /// <summary>
        /// Generates help products.
        /// </summary>
        /// <returns>Path to help project generated.</returns>
        private string Generate() 
        {

            // Open and process source files
            OpenSourceFiles();

            if (CancellRequested())
                return null;

            if( esWord )
            {
                // A�adir a la lista de archivos adicionales el directorio generado con 
                // los archivos del documento word:
                string[] archivos = Directory.GetDirectories( dirHtml );
                foreach( string archivo in archivos ) 
                    ArchivosAdicionales.Add( archivo );
            }

            if (CancellRequested())
                return null;

            // Preparar el directorio de destino.
            log("Creating project directory: " + Project.HelpProjectDirectory, ConsoleUserInterface.INFO);
            ArrayList listaFinalArchivos = GenerarDirDestino(Project.HelpProjectDirectory);

            // Check if there is a <STYLE> tag into the header. If there is, take it out to a CSS file.
            log("Extracting STYLE tags to a CSS file", ConsoleUserInterface.INFO);
            string cssFile = CheckForStyleTags();
            if (cssFile != null)
                listaFinalArchivos.Add(cssFile);

            if (CancellRequested())
                return null;

            PrepareHtmlDecorators();

            if (CancellRequested())
                return null;

            // Build the tree structure of chapters.
            log("Searching sections", ConsoleUserInterface.INFO);
            tree = new ArbolCapitulos();
            tree.AnalizarDocumento( Project.CutLevel, iDoc.body , this.UI );

            if (CancellRequested())
                return null;

            log("Splitting file", ConsoleUserInterface.INFO);
            // newBody is the <body> tag of the current splitted part 
            IHTMLElement newBody = Clone( iDoc.body );
            IHTMLElementCollection col = (IHTMLElementCollection)iDoc.body.children;
            // Traverse root nodes:
            foreach( IHTMLElement nodo in col ) 
            {
                if (IsCutHeader(nodo)) 
                {
                    // Found start of a new part: Store the current body part.
                    GuardarParte(newBody);
                    newBody = Clone(iDoc.body);
                    InsertAfter(newBody, nodo);
                }
                else 
                {
                    ArrayList lista = ProcessNode( nodo );
                    foreach( IHTMLElement hijo in lista ) 
                    {
                        InsertAfter(newBody, hijo);

                        if( lista[ lista.Count - 1 ] != hijo ) 
                        {
                            // Si no es el ultimo, cerrar esta parte y abrir otra.
                            GuardarParte(newBody);
                            newBody = Clone(iDoc.body);
                        }
                    }
                }

                if (CancellRequested())
                    return null;
            }
            GuardarParte(newBody);

            if (CancellRequested())
                return null;

            // Generar los archivos HTML:
            log("Storing splitted files", ConsoleUserInterface.INFO);
            ArrayList archivosGenerados = GuardarDocumentos(Project.HelpProjectDirectory, chmDecorator, null);

            if (CancellRequested())
                return null;

            // Mirar si al final se ha generado el archivo "1.htm". Si no, borrarlo
            // del arbol de archivos:
            string archivo1 = Project.HelpProjectDirectory + Path.DirectorySeparatorChar + "1.htm";
            if( ! File.Exists( archivo1) ) 
            {
                tree.Raiz.Archivo = "";
                tree.Raiz.Hijos.RemoveAt(0);
            }

            // Obtener el nombre del primer archivo generado:
            string primero = "";
            if( tree.Raiz.Hijos.Count > 0 )
                primero = ((NodoArbol) tree.Raiz.Hijos[0]).Archivo;

            if (CancellRequested())
                return null;

            // Generar archivo con arbol de contenidos:
            log("Generating table of contents", ConsoleUserInterface.INFO);
            tree.GenerarArbolDeContenidos(Project.HelpProjectDirectory + Path.DirectorySeparatorChar +
                "toc-generado.hhc", Project.MaxHeaderContentTree, helpWorkshopEncoding );
            
            if (CancellRequested())
                return null;

            // Generar archivo con palabras clave:
            log("Generating index", ConsoleUserInterface.INFO);
            Index index = tree.GenerarIndice(Project.MaxHeaderIndex);
            index.StoreHelpIndex(Project.HelpProjectDirectory + Path.DirectorySeparatorChar +
                "Index-generado.hhk", helpWorkshopEncoding);

            if (CancellRequested())
                return null;

            // Generar el archivo del proyecto de ayuda
            log("Generating help project", ConsoleUserInterface.INFO);
            string archivoAyuda = Project.HelpProjectDirectory + Path.DirectorySeparatorChar + NOMBREPROYECTO;
            GenerarArchivoProyecto( listaFinalArchivos , archivoAyuda , primero );

            if (CancellRequested())
                return null;

            if( Project.GenerateWeb )
            {
                // Generar la web con la ayuda:
                log("Generating web site", ConsoleUserInterface.INFO);
                GenerateWebSite(archivosGenerados, index, cssFile);
            }

            if (CancellRequested())
                return null;

            if (Project.GenerateJavaHelp)
            {
                log("Generating Java Help", ConsoleUserInterface.INFO);
                GenerateJavaHelp(archivosGenerados, index, cssFile);
            }

            if( esWord )
                // Era un doc word. Se creo un dir. temporal para guardar el html.
                // Borrar este directorio:
                Directory.Delete( dirHtml , true );

            log("Project generated", ConsoleUserInterface.ERRORWARNING);

            return archivoAyuda;
        }

        /// <summary>
        /// Get a HTML safe version of a text.
        /// </summary>
        /// <param name="textToEnconde">Text to use on a HTML content page.</param>
        /// <returns>The save HTML version of the text</returns>
        static public string HtmlEncode(string textToEnconde)
        {
            //return HtmlEncode(textToEnconde, true);
            return HttpUtility.HtmlEncode(textToEnconde);
        }

        private void GeneateSitemap(string webDirectory)
        {
            try {
                string sitemap = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + 
                                 "<urlset xmlns=\"http://www.google.com/schemas/sitemap/0.84\">\n";
                string webBase = this.Project.WebBase;
                if( !webBase.EndsWith("/") )
                    webBase += "/";
                if( !webBase.StartsWith("http://") )
                    webBase += "http://";

                string[] htmlFiles = Directory.GetFiles(webDirectory);
                foreach (string file in htmlFiles)
                {
                    string lowerFile = file.ToLower();
                    if (lowerFile.EndsWith(".htm") || lowerFile.EndsWith(".html"))
                    {
                        // Add to the sitemap
                        sitemap += "<url>\n<loc>" + webBase + Path.GetFileName(file) + "</loc>\n<lastmod>";
                        DateTime lastmod = File.GetLastWriteTime( file );
                        sitemap += lastmod.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'sszzz") + "</lastmod>\n";
                        sitemap += "<changefreq>" + this.Project.ChangeFrequency + "</changefreq>\n";
                        sitemap += "</url>\n";
                    }
                }
                sitemap += "</urlset>";

                // Store
                string sitemapFile = webDirectory + Path.DirectorySeparatorChar + "sitemap.xml";
                StreamWriter writer = new StreamWriter( sitemapFile , false , Encoding.UTF8 );
                writer.Write( sitemap );
                writer.Close();
                string sitemapZiped = webDirectory + Path.DirectorySeparatorChar + "sitemap.xml.gz";
                Zip.CompressFile(sitemapFile, sitemapZiped);
                File.Delete(sitemapFile);
            }
            catch( Exception ex ) {
                log("Error generating the sitemap: " + ex.Message, ConsoleUserInterface.ERRORWARNING);
                log(ex);
            }
        }

        /// <summary>
        /// Generates the help.hs help set xml file for java help
        /// </summary>
        /// <param name="dirJavaHelp">Directory where to generate the help.hs file</param>
        /// <param name="index">Index of topics of the document</param>
        void GenerateJavaHelpSetFile(String dirJavaHelp, Index index)
        {
            // TODO: Translate the labels with web translation files:
            StreamWriter writer = new StreamWriter( dirJavaHelp + Path.DirectorySeparatorChar + "help.hs" ,
                false, Encoding.UTF8 );
            writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            writer.WriteLine("<!DOCTYPE helpset\n" + 
                "PUBLIC \"-//Sun Microsystems Inc.//DTD JavaHelp HelpSet Version 2.0//EN\"\n" + 
                "\"http://java.sun.com/products/javahelp/helpset_2_0.dtd\">");
            writer.WriteLine("<helpset version=\"2.0\">");
            writer.WriteLine("<title>" + Project.HelpTitle + "</title>");
            writer.WriteLine("<maps><homeID>" + index.FirstTopicTarget + "</homeID><mapref location=\"map.jhm\"/></maps>");
            writer.WriteLine("<view><name>TOC</name><label>Table Of Contents</label><type>javax.help.TOCView</type><data>toc.xml</data></view>");
            writer.WriteLine("<view><name>Index</name><label>Index</label><type>javax.help.IndexView</type><data>index.xml</data></view>");
            writer.WriteLine("<view><name>Search</name><label>Search</label><type>javax.help.SearchView</type><data engine=\"com.sun.java.help.search.DefaultSearchEngine\">JavaHelpSearch</data></view>");
            writer.WriteLine("</helpset>");
            writer.Close();
        }

        

        /// <summary>
        /// Generates a JAR with the java help of the document.
        /// <param name="generatedFiles">List of chapter html files generated for the help</param>
        /// <param name="index">List of topics of the document.</param>
        /// <param name="cssFile">CSS file of the document, if it was generated.</param>
        /// </summary>
        private void GenerateJavaHelp(ArrayList generatedFiles, Index index, string cssFile)
        {
            
            // Create a temporal directy to generate the javahelp files:
            String dirJavaHelp = Path.GetTempPath() + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(MainSourceFile) + "-javahelp";
            log("Copiying files to directory " + dirJavaHelp, ConsoleUserInterface.INFO);
            GenerarDirDestino(dirJavaHelp);

            // Copy the css file if was generated:
            if (cssFile != null)
                File.Copy(cssFile, dirJavaHelp + Path.DirectorySeparatorChar + Path.GetFileName(cssFile));

            /*
            // Copy files generated for the CHM help
            foreach (string file in generatedFiles )
            {
                string dstFile = dirJavaHelp + Path.DirectorySeparatorChar + Path.GetFileName(file);
                File.Copy(file, dstFile);
            }
            */

            // Write HTML help content files to the destination directory
            GuardarDocumentos(dirJavaHelp, webDecorator, null);

            log("Generating java help xml files", ConsoleUserInterface.INFO);
            // Generate the java help xml files:
            GenerateJavaHelpSetFile(dirJavaHelp, index);
            index.GenerateJavaHelpIndex(dirJavaHelp + Path.DirectorySeparatorChar + "index.xml");
            index.GenerateJavaHelpMapFile(dirJavaHelp + Path.DirectorySeparatorChar + "map.jhm");
            tree.GenerateJavaHelpTOC(dirJavaHelp + Path.DirectorySeparatorChar + "toc.xml", Project.MaxHeaderContentTree);

            log("Building the search index", ConsoleUserInterface.INFO);
            log(AppSettings.JavaHelpIndexerPath + " .", ConsoleUserInterface.INFO);
            ExecuteCommandLine(AppSettings.JavaHelpIndexerPath, ".", dirJavaHelp);

            // Build a JAR with the help.
            //java -jar E:\dev\java\javahelp\javahelp2.0\demos\bin\hsviewer.jar -helpset help.jar
            string commandLine = " cvf \"" + Project.JavaHelpPath + "\" .";
            string jarPath = AppSettings.JarPath;
            log("Building jar:", ConsoleUserInterface.INFO);
            log(jarPath + " " + commandLine, ConsoleUserInterface.INFO);
            ExecuteCommandLine(jarPath, commandLine, dirJavaHelp);

            Directory.Delete(dirJavaHelp, true);
        }

        /// <summary>
        /// Executes a command line and writes the command output to the log.
        /// </summary>
        /// <param name="exeFile">Path of the executable file to run</param>
        /// <param name="parameters">Parameters of the command line</param>
        /// <param name="workingDirectory">Directory where to run the command line</param>
        private void ExecuteCommandLine(string exeFile, string parameters, string workingDirectory)
        {
            ProcessStartInfo info = new ProcessStartInfo(exeFile, parameters);
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.CreateNoWindow = true;
            info.WorkingDirectory = workingDirectory;

            Process proceso = Process.Start(info);
            while (!proceso.WaitForExit(1000))
                LogStream(proceso.StandardOutput, ConsoleUserInterface.INFO);
            LogStream(proceso.StandardOutput, ConsoleUserInterface.INFO);
        }

        /// <summary>
        /// Generated the help web site 
        /// </summary>
        /// <param name="archivosGenerados">List of all files of the help content.</param>
        /// <param name="index">Index help information</param>
        /// <param name="cssFile">File that contains extracted CSS styles</param>
        private void GenerateWebSite( ArrayList archivosGenerados, Index index, string cssFile ) 
        {
            try
            {
                // Crear el directorio web y copiar archivos adicionales:
                string dirWeb;
                //if( DirectorioWeb.Equals("") )
                if (Project.WebDirectory.Equals(""))
                    dirWeb = Project.HelpProjectDirectory + Path.DirectorySeparatorChar + "web";
                else
                    dirWeb = Project.WebDirectory;
                GenerarDirDestino(dirWeb);

                // Copy the css file if was generated:
                if (cssFile != null)
                    File.Copy(cssFile, dirWeb + Path.DirectorySeparatorChar + Path.GetFileName(cssFile));

                // Prepare the indexing database:
                WebIndex indexer = null;
                try
                {
                    if (Project.FullTextSearch)
                    {
                        indexer = new WebIndex();
                        string dbFile = dirWeb + Path.DirectorySeparatorChar + "fullsearchdb.db3";
                        string dirTextFiles = dirWeb + Path.DirectorySeparatorChar + "textFiles";
                        indexer.Connect(dbFile);
                        indexer.CreateDatabase(System.Windows.Forms.Application.StartupPath + Path.DirectorySeparatorChar + "searchdb.sql", dirTextFiles);
                        indexer.StoreConfiguration(Project.WebLanguage);
                    }

                    // Create new files for the web help:
                    GuardarDocumentos(dirWeb, webDecorator, indexer);
                }
                finally
                {
                    if (indexer != null)
                        indexer.Disconnect();
                }

                // HTML save version of the title:
                string htmlTitle = HtmlEncode(Project.HelpTitle);

                // Generate search form HTML code:
                string textSearch = "";
                if (Project.FullTextSearch)
                {
                    textSearch = "<form name=\"searchform\" method=\"post\" action=\"search.aspx\" id=\"searchform\" onsubmit=\"doFullTextSearch();return false;\" >\n";
                    textSearch += "<p><img src=\"system-search.png\" align=middle alt=\"Search image\" /> <b>%Search Text%:</b><br /><input type=\"text\" id=\"searchText\" style=\"width:80%;\" name=\"searchText\"/>\n";
                    textSearch += "<input type=\"button\" value=\"%Search%\" onclick=\"doFullTextSearch();\" id=\"Button1\" name=\"Button1\"/></p>\n";
                }
                else
                {
                    textSearch = "<form name=\"searchform\" method=\"post\" action=\"search.aspx\" id=\"searchform\" onsubmit=\"doSearch();return false;\" >\n";
                    textSearch += "<p><img src=\"system-search.png\" align=middle alt=\"Search image\" /> <b>%Search Text%:</b><br /><input type=\"text\" id=\"searchText\" style=\"width:80%;\" name=\"searchText\"/><br/>\n";
                    textSearch += "<input type=\"button\" value=\"%Search%\" onclick=\"doSearch();\" id=\"Button1\" name=\"Button1\"/></p>\n";
                    textSearch += "<select id=\"searchResult\" style=\"width:100%;\" size=\"20\" name=\"searchResult\">\n";
                    textSearch += "<option></option>\n";
                    textSearch += "</select>\n";
                }
                textSearch += "</form>\n";

                // The text placements for web files:
                string[] variables = { "%TEXTSEARCH%" , "%TITLE%", "%TREE%", "%TOPICS%", "%FIRSTPAGECONTENT%", 
                    "%WEBDESCRIPTION%", "%KEYWORDS%" , "%HEADER%" , "%FOOTER%" , "%HEADINCLUDE%" };
                string[] newValues = { textSearch , htmlTitle, tree.GenerarArbolHtml(Project.MaxHeaderContentTree, "contentsTree", 
                    "contentTree"), index.GenerateWebIndex(), FirstChapterContent, 
                    webDecorator.MetaDescriptionTag , webDecorator.MetaKeywordsTag ,
                    webDecorator.HeaderHtmlCode , webDecorator.FooterHtmlCode , webDecorator.HeadIncludeHtmlCode };

                Replacements replacements = new Replacements(variables, newValues);

                // Load translation files.
                string translationFile = System.Windows.Forms.Application.StartupPath +
                    Path.DirectorySeparatorChar + "webTranslations" + Path.DirectorySeparatorChar +
                    Project.WebLanguage + ".txt";
                try
                {
                    replacements.AddReplacementsFromFile(translationFile);
                }
                catch (Exception ex)
                {
                    log("Error opening web translations file" + translationFile + ": " + ex.Message, ConsoleUserInterface.ERRORWARNING);
                    log(ex);
                }

                // Copy web files replacing text
                string baseDir = System.Windows.Forms.Application.StartupPath + Path.DirectorySeparatorChar + "webFiles";
                replacements.CopyDirectoryReplaced(baseDir, dirWeb, MSWord.HTMLEXTENSIONS, AppSettings.UseTidyOverOutput, UI, webDecorator.OutputEncoding);

                // Copy full text search files replacing text:
                if (Project.FullTextSearch)
                {
                    // Copy full text serch files:
                    string dirSearchFiles = System.Windows.Forms.Application.StartupPath + Path.DirectorySeparatorChar + "searchFiles";
                    replacements.CopyDirectoryReplaced(dirSearchFiles, dirWeb, MSWord.ASPXEXTENSIONS, false, UI, webDecorator.OutputEncoding);
                }

                if (Project.GenerateSitemap)
                    // Generate site map for web indexers (google).
                    GeneateSitemap(dirWeb);

            }
            catch (Exception ex)
            {
                log(ex);
            }
        }

        private void GenerarArchivoProyecto( ArrayList archivosAdicinales , string archivo , string temaInicial) 
        {
            StreamWriter writer = new StreamWriter(archivo, false, helpWorkshopEncoding);
            writer.WriteLine( "[OPTIONS]" );
            writer.WriteLine( "Compatibility=1.1 or later" );
            writer.WriteLine( "Compiled file=" + NOMBREARCHIVOAYUDA );
            writer.WriteLine( "Contents file=toc-generado.hhc" );
            writer.WriteLine( "Default topic=" + temaInicial );
            writer.WriteLine( "Display compile progress=No" );
            writer.WriteLine( "Full-text search=Yes" );
            writer.WriteLine( "Index file=Index-generado.hhk" );
            //writer.WriteLine( "Language=0xc0a Espa�ol (alfabetizaci�n internacional)" );
            //writer.WriteLine( "Language=0x0409 English (UNITED STATES)" );
            writer.WriteLine("Language=0x" + Convert.ToString(helpWorkshopCulture.LCID, 16) + " " + helpWorkshopCulture.DisplayName );
            writer.WriteLine( "Title=" + Project.HelpTitle );
            writer.WriteLine( "\r\n[FILES]" );
            foreach( string archivoAdi in archivosAdicinales )
                writer.WriteLine( archivoAdi );
            ArrayList lista = tree.ListaArchivosGenerados();
            foreach( string arc in lista )
                writer.WriteLine( arc );
            writer.WriteLine( "\r\n[INFOTYPES]\r\n" );
            writer.Close();
        }

        /// <summary>
        /// Return the first header tag (H1,H2,etc) found on a subtree of the html document 
        /// that will split the document.
        /// </summary>
        /// <param name="root">Root of the html subtree where to search a split</param>
        /// <returns>The first split tag node. null if none was found.</returns>
        private IHTMLElement SearchFirstCutNode( IHTMLElement root ) 
        {
            if (IsCutHeader(root))
                return root;
            else 
            {
                IHTMLElementCollection col = (IHTMLElementCollection)root.children;
                foreach( IHTMLElement e in col ) 
                {
                    IHTMLElement seccion = SearchFirstCutNode( e );
                    if( seccion != null )
                        return seccion;
                }
                return null;
            }
        }

        private IHTMLAnchorElement BuscarNodoA( IHTMLElement raiz ) 
        {
            if( raiz is IHTMLAnchorElement )
                return (IHTMLAnchorElement)raiz;
            else 
            {
                IHTMLElementCollection col = (IHTMLElementCollection)raiz.children;
                foreach( IHTMLElement e in col ) 
                {
                    IHTMLAnchorElement seccion = BuscarNodoA( e );
                    if( seccion != null )
                        return seccion;
                }
                return null;
            }

        }

        /// <summary>
        /// Busca si un arbol contiene un corte de seccion.
        /// </summary>
        /// <param name="nodo">Raiz del arbol en que buscar</param>
        /// <returns>True si el arbol contiene un corte de seccion.</returns>
        private bool WillBeBroken( IHTMLElement nodo ) 
        {
            return SearchFirstCutNode( nodo ) != null;
        }

        /// <summary>
        /// Clone a node, without their children.
        /// </summary>
        /// <param name="nodo">Node to clone</param>
        /// <returns>Cloned node</returns>
        private IHTMLElement Clone(IHTMLElement nodo ) 
        {
            IHTMLElement e = iDoc.createElement( nodo.tagName );
            IHTMLElement2 e2 = (IHTMLElement2) e;
            e2.mergeAttributes( nodo );
            return e;
        }

        static public bool EsHeader( IHTMLElement nodo ) 
        {
            return nodo is IHTMLHeaderElement && nodo.innerText != null && !nodo.innerText.Trim().Equals("");
        }

        /// <summary>
        /// Checks if a node is a HTML header tag (H1, H2, etc) upper or equal to the cut level for the
        /// project (Project.CutLevel).
        /// Also checks if it contains some text.
        /// </summary>
        /// <param name="node">HTML node to check</param>
        /// <returns>true if the node is a cut header</returns>
        public bool IsCutHeader( IHTMLElement node ) {
            return IsCutHeader(Project.CutLevel, node);
        }

        /// <summary>
        /// Checks if a node is a HTML header tag (H1, H2, etc) upper or equal to the cut level.
        /// Also checks if it contains some text.
        /// </summary>
        /// <param name="MaximumLevel">Maximum level the level is accepted as cut level.</param>
        /// <param name="node">HTML node to check</param>
        /// <returns>true if the node is a cut header</returns>
        static public bool IsCutHeader( int MaximumLevel , IHTMLElement node ) 
        {
            // If its a Hx node and x <= MaximumLevel, and it contains text, its a cut node:
            if( EsHeader(node) ) 
            {
                string tagName = node.tagName.ToUpper();
                for( int i=1;i<=MaximumLevel; i++ ) 
                {
                    string nombreTag = "H" + i;
                    if( nombreTag.Equals( tagName ) )
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Adds a HTML node as child of other.
        /// The child node is added at the end of the list of parent children.
        /// </summary>
        /// <param name="parent">Parent witch to add the new node</param>
        /// <param name="child">The child node to add</param>
        private void InsertAfter( IHTMLElement parent , IHTMLElement child ) 
        {
            try 
            {
                ((IHTMLDOMNode)parent).appendChild((IHTMLDOMNode)child);
            }
            catch( Exception ex ) {
                log("Warning: error adding a child (" + child.tagName + ") to his parent (" +
                     parent.tagName + "): " + ex.Message, ConsoleUserInterface.ERRORWARNING);
                log(ex);
            }
        }

        /// <summary>
        /// Process a HTML node of the document
        /// </summary>
        /// <param name="node">Node to process</param>
        /// <returns>
        /// A list of subtrees of the HTML original tree broken by the cut level headers.
        /// If the node and their descendands have not cut level headers, the node will be returned as is.
        /// </returns>
        private ArrayList ProcessNode( IHTMLElement node ) 
        {
            ArrayList subtreesList = new ArrayList();

            // Check if the node will be broken on more than one piece because it contains a cut level
            // header:
            if( WillBeBroken( node ) ) 
            {
                // It contains a cut level.
                IHTMLElementCollection children = (IHTMLElementCollection)node.children;
                IHTMLElement newNode = Clone( node );
                foreach( IHTMLElement e in children ) 
                {
                    if (IsCutHeader(e))
                    {
                        // A cut header was found. Cut here.
                        subtreesList.Add( newNode );
                        newNode = Clone( node );
                        InsertAfter( newNode , e );
                    }
                    else 
                    {
                        ArrayList listaHijos = ProcessNode( e );
                        foreach( IHTMLElement hijo in listaHijos ) 
                        {
                            InsertAfter( newNode , hijo );
                            if( listaHijos[ listaHijos.Count - 1 ] != hijo ) 
                            {
                                // Si no es el ultimo, cerrar esta parte y abrir otra.
                                subtreesList.Add( newNode );
                                newNode = Clone( node );
                            }
                        }
                    }
                }
                subtreesList.Add( newNode );
            }
            else 
                // The node and their children will not broken because it does not contains any
                // cut level (or upper) header title. So, add the node and their children as they are:
                subtreesList.Add( node );

            return subtreesList;
        }

        /// <summary>
        /// Logs the content of a stream
        /// </summary>
        /// <param name="reader">Log with the content to read.</param>
        /// <param name="level">Level of the stream</param>
        private void LogStream(StreamReader reader, int logLevel) 
        {
            /*string linea = reader.ReadLine();
            while( linea != null ) 
            {
                log(linea, ConsoleUserInterface.INFO);
                linea = reader.ReadLine();
            }*/
            if (UI != null)
                UI.LogStream(reader, logLevel);
        }

        /// <summary>
        /// Compiles the help project file and it's copied to the destination file.
        /// </summary>
        /// <param name="helpFile">Project help project full path.</param>
        /// <param name="compilerPath">Compiler exe (hhc.exe) full path.</param>
        private void Compile( string helpFile , string compilerPath ) 
        {
            log("Compiling", ConsoleUserInterface.INFO);
            if( ! File.Exists( compilerPath ) )
                throw new Exception("Compiler not found at " + compilerPath + ". Help not generated");
            else 
            {
                string proyecto = "\"" + Project.HelpProjectDirectory + Path.DirectorySeparatorChar + NOMBREPROYECTO + "\"";

                ProcessStartInfo info;
                if (!AppSettings.UseAppLocale)
                    // Run the raw compiler
                    info = new ProcessStartInfo(compilerPath, proyecto);
                else
                {
                    // Run the compiler with AppLocale. Need to compile files with a 
                    // char encoding distinct to the system codepage.
                    // Command line example: C:\Windows\AppPatch\AppLoc.exe "C:\Program Files\HTML Help Workshop\hhc.exe" "A B C" "/L0480"
                    string parameters = "\"" + compilerPath + "\" " + proyecto + " /L" + Convert.ToString(helpWorkshopCulture.LCID, 16);
                    info = new ProcessStartInfo(AppSettings.AppLocalePath, parameters);
                }

                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.CreateNoWindow = true;
                Process proceso = Process.Start( info );
                while( ! proceso.WaitForExit( 1000 ) )
                    LogStream(proceso.StandardOutput, ConsoleUserInterface.INFO);
                LogStream(proceso.StandardOutput, ConsoleUserInterface.INFO);

                string archivoAyudaOrigen = Project.HelpProjectDirectory + Path.DirectorySeparatorChar + NOMBREARCHIVOAYUDA;
                if( File.Exists( archivoAyudaOrigen ) ) 
                    // Copy the file frrom the temporally directory to the gift by the user
                    File.Copy( archivoAyudaOrigen , helpFile , true );
                else 
                    throw new Exception("Some error happened with the compilation. Try to generate the help project");
            }
        }
    }
}

