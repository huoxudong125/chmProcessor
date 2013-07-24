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
using System.Web;
using System.Text;

namespace ChmProcessorLib
{
	/// <summary>
	/// Estructura de arbol de los capitulos dentro de un documento.
	/// </summary>
    public class ArbolCapitulos
    {

        /// <summary>
        /// Default title for the start of the help content (start of the document out of any section,
        /// entire help content on a single page, etc)
        /// </summary>
        static public String DEFAULTTILE = "Help content";

        /// <summary>
        /// Ultimo nodo insertado en el arbol.
        /// </summary>
        private NodoArbol ultimoInsertado;

        // TODO: This should be a private member
        public NodoArbol Raiz;

        public ArbolCapitulos()
        {
            Raiz = new NodoArbol( null , null , null );
            Raiz.Nivel = 0;
        }

        /// <summary>
        /// Adds a section to the section tree.
        /// The section will be added as child of the last section inserted with a level
        /// higher than then section
        /// </summary>
        /// <param name="nodo">HTML header tag with the title of the section</param>
        /// <param name="ui">Application log. It can be null</param>
        public void InsertarNodo( IHTMLElement node , UserInterface ui ) 
        {
            // Ignorar cabeceras vacias (saltos de linea,etc. ) :
            if( !DocumentProcessor.EsHeader( node ) )
                return;

            int nivel = NodoArbol.NivelNodo( node );
            if( ultimoInsertado == null || nivel == 1 ) 
            {
                ultimoInsertado = new NodoArbol( null, node , ui );
                Raiz.NuevoHijo( ultimoInsertado );
            }
            else 
            {
                NodoArbol nuevoNodo = new NodoArbol( ultimoInsertado, node , ui );
                if( ultimoInsertado.Nivel < nivel )
                    ultimoInsertado.Hijos.Add( nuevoNodo );
                else 
                {
                    NodoArbol actual = ultimoInsertado.Padre;
                    while( actual != Raiz && actual.Nivel >= nivel )
                        actual = actual.Padre;
                    actual.NuevoHijo( nuevoNodo );
                }
                ultimoInsertado = nuevoNodo;
            }
        }

        protected void GenerarArbolDeContenidos( StreamWriter writer , NodoArbol nodo , int NivelMaximoTOC , int nivel ) 
        {
            if( NivelMaximoTOC != 0 && nivel > NivelMaximoTOC )
                return;

            writer.WriteLine( nodo.EntradaArbolContenidos );
            if( nodo.Hijos.Count > 0 ) 
            {
                writer.WriteLine( "<UL>" );
                foreach( NodoArbol hijo in nodo.Hijos ) 
                    GenerarArbolDeContenidos( writer , hijo , NivelMaximoTOC , nivel + 1 );
                writer.WriteLine( "</UL>" );
            }
        }

        /// <summary>
        /// Saves the table of contents of this tree for a CHM project.
        /// </summary>
        /// <param name="filePath">Path where to store the file.</param>
        /// <param name="MaxTOCLevel">Maximum level of deepth into the tree to save sections. =0 
        /// will save all the sections</param>
        /// <param name="encoding">Encoding to save the file</param>
        public void GenerarArbolDeContenidos( string filePath , int MaxTOCLevel , Encoding encoding) 
        {
            StreamWriter writer = new StreamWriter(filePath, false, encoding);
            writer.WriteLine( "<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML//EN\">" );
            writer.WriteLine( "<HTML>" );
            writer.WriteLine( "<HEAD>" );
            writer.WriteLine( "<!-- Sitemap 1.0 -->" );
            writer.WriteLine( "</HEAD><BODY>" );
            writer.WriteLine( "<UL>" );
            foreach( NodoArbol hijo in Raiz.Hijos ) 
                GenerarArbolDeContenidos( writer , hijo , MaxTOCLevel , 1 );
            writer.WriteLine( "</UL>" );
            writer.WriteLine( "</BODY></HTML>" );
            writer.Close();
        }

        /// <summary>
        /// Generate a java help table of contents xml file.
        /// </summary>
        /// <param name="writer">File where to store the TOC</param>
        /// <param name="currentNode">Node to process now</param>
        /// <param name="currentLevel">Current deep level of the node into the document tree</param>
        /// <param name="maxLevelTOC">Maximum deep level into the tree to generate the TOC.</param>
        public void GenerateJavaHelpTOC(StreamWriter writer, NodoArbol currentNode, int maxLevelTOC, int currentLevel)
        {
            if (maxLevelTOC != 0 && currentLevel > maxLevelTOC)
                return;

            if( currentNode.Nodo != null ) 
                writer.WriteLine(currentNode.JavaHelpTOCEntry);
            foreach (NodoArbol child in currentNode.Hijos)
                GenerateJavaHelpTOC(writer, child, maxLevelTOC, currentLevel + 1);

            if (currentNode.Nodo != null && currentNode.Hijos.Count > 0)
                writer.WriteLine("</tocitem>");
        }

        /// <summary>
        /// Generate a java help table of contents xml file.
        /// </summary>
        /// <param name="file">Path of the TOC file to generate.</param>
        /// <param name="maxLevelTOC">Maximum deep level into the tree to generate the TOC.</param>
        public void GenerateJavaHelpTOC(string file, int maxLevelTOC)
        {
            StreamWriter writer = new StreamWriter(file, false, Encoding.UTF8);
            writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            writer.WriteLine("<!DOCTYPE toc\n" + 
                "PUBLIC \"-//Sun Microsystems Inc.//DTD JavaHelp TOC Version 2.0//EN\"\n" +
                "\"http://java.sun.com/products/javahelp/toc_2_0.dtd\">");
            writer.WriteLine("<toc version=\"2.0\">");
            foreach (NodoArbol child in Raiz.Hijos)
                GenerateJavaHelpTOC(writer, child, maxLevelTOC, 1);
            writer.WriteLine("</toc>");
            writer.Close();
        }

        public void GenerarIndice( Index index , NodoArbol nodo , int NivelMaximoIndice , int nivel ) 
        {
            if( NivelMaximoIndice != 0 && nivel > NivelMaximoIndice )
                return;

            index.AddNode( nodo );
            //writer.WriteLine( nodo.EntradaArbolContenidos );
            foreach( NodoArbol hijo in nodo.Hijos ) 
                GenerarIndice( index , /*writer ,*/ hijo , NivelMaximoIndice , nivel + 1 );
        }

        public Index GenerarIndice( int NivelMaximoIndice ) 
        {
            Index index = new Index();
            foreach( NodoArbol hijo in Raiz.Hijos ) 
                GenerarIndice( index , hijo , NivelMaximoIndice , 1 );
            return index;
        }

        private string GenerarArbolHtml( NodoArbol nodo , int NivelMaximoTOC , int nivel ) 
        {
            if( NivelMaximoTOC != 0 && nivel > NivelMaximoTOC )
                return "";

            string texto = "";
            if( ! nodo.Href.Equals("") ) 
            {
                // Verificar el nodo inicial, que puede no tener titulo:
                string nombre = "";
                if( nodo.Nodo != null )
                    nombre = nodo.Nodo.innerText;
                else
                    nombre = DEFAULTTILE;
                texto = "<li><a href=\"" + nodo.Href ;
                texto += "\">" + DocumentProcessor.HtmlEncode( nombre ) + "</a>";
            }

            if( nodo.Hijos.Count > 0 ) 
            {
                if( NivelMaximoTOC == 0 || nivel < NivelMaximoTOC ) 
                {
                    texto += "\n<ul>\n";
                    foreach( NodoArbol hijo in nodo.Hijos ) 
                        texto += GenerarArbolHtml( hijo , NivelMaximoTOC , nivel + 1 ) + "\n";
                    texto += "</ul>";
                }
            }
            if( !texto.Equals("") )
                texto += "</li>";
            return texto;
        }

        public string GenerarArbolHtml(int NivelMaximoTOC , string id , string classId ) 
        {
            //string texto = "<ul id=\"contentsTree\" class=\"contentTree\">";
            string texto = "<ul";
            if( id != null )
                texto += " id=\"" + id + "\"";
            if( classId != null )
                texto += " class=\"" + classId + "\"";
            texto += ">\n";

            foreach( NodoArbol hijo in Raiz.Hijos ) 
                texto += GenerarArbolHtml( hijo , NivelMaximoTOC , 1 ) + "\n";
            texto += "</ul>\n";
            return texto;
        }

        private void AsignarNombreArchivos( NodoArbol nodo , ref int Cnt , int nivelCorte ) 
        {
            if( nodo.Nodo != null && DocumentProcessor.IsCutHeader( nivelCorte , nodo.Nodo ) ) 
                nodo.StoredAt( nodo.NombreArchivo( Cnt++ ) );

            foreach( NodoArbol hijo in nodo.Hijos ) 
                AsignarNombreArchivos( hijo , ref Cnt , nivelCorte );
        }

        /// <summary>
        /// Make a recursive seach of all HTML header nodes into the document.
        /// </summary>
        /// <param name="currentNode">Current HTML node on the recursive search</param>
        /// <param name="ui">The application log. It can be null.</param>
        private void AnalizarDocumentoRecursivo( IHTMLElement currentNode , UserInterface ui ) 
        {
            if( currentNode is IHTMLHeaderElement )
                InsertarNodo( currentNode , ui );

            IHTMLElementCollection col = (IHTMLElementCollection) currentNode.children;
            foreach( IHTMLElement hijo in col )
                AnalizarDocumentoRecursivo(hijo, ui);
        }

        /// <summary>
        /// Builds the sections tree of the document.
        /// </summary>
        /// <param name="cutLevel">HTML Header level that will contain an entire HTML page</param>
        /// <param name="root">Root of the HTML document</param>
        /// <param name="ui">The application log. it can be null.</param>
        public void AnalizarDocumento( int cutLevel , IHTMLElement root , UserInterface ui) 
        {
            // Reservar el primer nodo para el contenido que venga sin titulo1, (portada,etc).
            NodoArbol sinSeccion = new NodoArbol( this.Raiz , null , ui);
            this.Raiz.Hijos.Add( sinSeccion );

            // Analizar que nodos de headers se encuentran en el documento
            AnalizarDocumentoRecursivo( root , ui );

            // Por defecto, todos los nodos al documento por defecto. El resto
            // ya ira cogiendo el valor de su archivo:
            this.Raiz.StoredAt( "1.htm" );

            // Guardar en cada nodo en que archivo se habra guardado el nodo:
            int Cnt = 2;
            foreach( NodoArbol hijo in this.Raiz.Hijos )
                AsignarNombreArchivos(hijo, ref Cnt, cutLevel);
        }

        private void ListaArchivosGenerados( ArrayList lista , NodoArbol nodo ) 
        {
            if( !nodo.Archivo.Equals("") && !lista.Contains(nodo.Archivo) )
                lista.Add( nodo.Archivo);
            foreach( NodoArbol hijo in nodo.Hijos )
                ListaArchivosGenerados( lista , hijo );
        }

        /// <summary>
        /// Obtiene la lista de archivos HTML que se generaran.
        /// </summary>
        /// <returns>Lista de strings con los nombres de los archivos generados.</returns>
        public ArrayList ListaArchivosGenerados() 
        {
            ArrayList lista = new ArrayList();
            ListaArchivosGenerados( lista , this.Raiz );
            return lista;
        }

        /// <summary>
        /// Searches the first section with a given title. 
        /// The comparation is done without letter case.
        /// </summary>
        /// <param name="sectionTitle">The section title to seach</param>
        /// <returns>The first section of the document with that title. null if no section was
        /// found.</returns>
        public NodoArbol SearchBySectionTitle(string sectionTitle)
        {
            return Raiz.SearchBySectionTitle(sectionTitle);
        }
    }
}
