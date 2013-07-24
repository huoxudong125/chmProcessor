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
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32;
using System.IO;

namespace ChmProcessorLib
{
    /// <summary>
    /// Application settings.
    /// </summary>
    public class AppSettings
    {
        static private string COMPILERKEY = "compilerpath";
        static private string USETIDYOVERINPUT = "usetidyinput";
        static private string USETIDYOVEROUTPUT = "usetidyoutput";
        static private string JDKHOME = "jdkhome";
        static private string JAVAHELPPATH = "javahelppath";

        /// <summary>
        /// Windows registry leaf where the program stores settings
        /// </summary>
        static public string KEY = "Software\\chmProcessor";

        /// <summary>
        /// The jar.exe absolute path on the system.
        /// </summary>
        static public string JarPath
        {
            get
            {
                return JdkHome + Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + "jar.exe";
            }
        }

        /// <summary>
        /// The java.exe absolute path on the system.
        /// </summary>
        static public string JavaPath
        {
            get
            {
                return JdkHome + Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + "java.exe";
            }
        }

        /// <summary>
        /// The JDK directory
        /// </summary>
        static public string JdkHome
        {
            get
            {
                string path = "";
                try
                {
                    RegistryKey rk = Registry.CurrentUser.CreateSubKey(KEY);
                    path = (string)rk.GetValue(JDKHOME, "");
                }
                catch
                {
                    path = "";
                }

                if (path.Equals(""))
                {
                    // Try to get the default place for the JDK:
                    string javaDefautDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + 
                        Path.DirectorySeparatorChar + "Java" ;
                    if( Directory.Exists(javaDefautDir) ) 
                    {
                        // Search jdks directories, and get the highest version.
                        string[] jdks = Directory.GetDirectories(javaDefautDir,"jdk*");
                        if(jdks.Length > 0) {
                            Array.Sort(jdks);
                            if( File.Exists( jdks[jdks.Length-1] + Path.DirectorySeparatorChar + "bin" + 
                                Path.DirectorySeparatorChar + "jar.exe" ) )
                                path = jdks[jdks.Length-1];
                        }
                    }
                }

                return path;
            }

            set
            {
                try
                {
                    RegistryKey rk = Registry.CurrentUser.OpenSubKey(KEY, true);
                    rk.SetValue(JDKHOME, value);
                }
                catch { }
            }
        }

        /// <summary>
        /// The path to the indexer jhindexer.bat of java help to build the full text search index.
        /// </summary>
        static public string JavaHelpIndexerPath
        {
            get
            {
                char sp = Path.DirectorySeparatorChar;
                return JavaHelpPath + sp + "javahelp" + sp + "bin" + sp + "jhindexer.bat";
            }
        }

        /// <summary>
        /// The path to the Java Help installation directory.
        /// </summary>
        static public string JavaHelpPath
        {
            get
            {
                string path = "";
                try
                {
                    RegistryKey rk = Registry.CurrentUser.CreateSubKey(KEY);
                    path = (string)rk.GetValue(JAVAHELPPATH, "");
                }
                catch
                {
                    path = "";
                }
                return path;
            }

            set
            {
                try
                {
                    RegistryKey rk = Registry.CurrentUser.OpenSubKey(KEY, true);
                    rk.SetValue(JAVAHELPPATH, value);
                }
                catch { }
            }
        }

        /// <summary>
        /// Path to the hsviewer.jar needed to view java help files.
        /// </summary>
        static public string JavaHelpViewerJar
        {
            get
            {
                char sp = Path.DirectorySeparatorChar;
                return JavaHelpPath + sp + "demos" + sp + "bin" + sp + "hsviewer.jar";
            }
        }

        /// <summary>
        /// The Microsoft help workshomp compiler path 
        /// </summary>
        /// <returns>The path to the compiler. "" if its not found</returns>
        static public string CompilerPath
        {
            get {
                string path = "";
                try
                {
                    RegistryKey rk = Registry.CurrentUser.CreateSubKey(KEY);
                    path = (string)rk.GetValue(COMPILERKEY, "");
                }
                catch
                {
                    path = "";
                }

                if (path.Equals(""))
                {
                    // Try to get the default place for the compiler:
                    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    string defaultPath = programFiles + Path.DirectorySeparatorChar + "HTML Help Workshop" +
                        Path.DirectorySeparatorChar + "hhc.exe";
                    if (File.Exists(defaultPath))
                    {
                        path = defaultPath;
                        CompilerPath = path;
                    }
                }
                return path;
            }

            set {
                try
                {
                    RegistryKey rk = Registry.CurrentUser.OpenSubKey(KEY, true);
                    rk.SetValue(COMPILERKEY, value);
                }
                catch { }
            }
        }


        static public bool UseTidyOverInput 
        {
            get
            {
                string value = "";
                try
                {
                    RegistryKey rk = Registry.CurrentUser.CreateSubKey(KEY);
                    value = (string)rk.GetValue(USETIDYOVERINPUT, "");
                }
                catch
                {
                    value = "";
                }

                if (value.Equals(""))
                {
                    value = "false";
                    UseTidyOverInput = false;
                }
                if (value.Equals("true"))
                    return true;
                else
                    return false;
            }

            set
            {
                try
                {
                    RegistryKey rk = Registry.CurrentUser.OpenSubKey(KEY, true);
                    string txt;
                    if (value)
                        txt = "true";
                    else
                        txt = "false";
                    rk.SetValue(USETIDYOVERINPUT, txt);
                }
                catch { }
            }
        }

        static public bool UseTidyOverOutput
        {
            get
            {
                string value = "";
                try
                {
                    RegistryKey rk = Registry.CurrentUser.CreateSubKey(KEY);
                    value = (string)rk.GetValue(USETIDYOVEROUTPUT, "");
                }
                catch
                {
                    value = "";
                }

                if (value.Equals(""))
                {
                    value = "true";
                    UseTidyOverOutput = true;
                }
                if (value.Equals("true"))
                    return true;
                else
                    return false;
            }

            set
            {
                try
                {
                    RegistryKey rk = Registry.CurrentUser.OpenSubKey(KEY, true);
                    string txt;
                    if (value)
                        txt = "true";
                    else
                        txt = "false";
                    rk.SetValue(USETIDYOVEROUTPUT, txt);
                }
                catch { }
            }
        }

    }
}
