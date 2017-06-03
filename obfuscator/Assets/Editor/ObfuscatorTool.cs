/*
               #########                       
              ############                     
              #############                    
             ##  ###########                   
            ###  ###### #####                  
            ### #######   ####                 
           ###  ########## ####                
          ####  ########### ####               
         ####   ###########  #####             
        #####   ### ########   #####           
       #####   ###   ########   ######         
      ######   ###  ###########   ######       
     ######   #### ##############  ######      
    #######  #####################  ######     
    #######  ######################  ######    
   #######  ###### #################  ######   
   #######  ###### ###### #########   ######   
   #######    ##  ######   ######     ######   
   #######        ######    #####     #####    
    ######        #####     #####     ####     
     #####        ####      #####     ###      
      #####       ###        ###      #        
        ###       ###        ###               
         ##       ###        ###               
__________#_______####_______####______________

                我们的未来没有BUG              
* ==============================================================================
* Filename: ObfuscatorTool.cs
* Created:  2017/6/3 16:24:48
* Author:   HaYaShi ToShiTaKa
* Purpose:  Obfuscator code api
* ==============================================================================
*/

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SharpObfuscator.Obfuscation2 {
    public class ObfuscatorTool {

        #region obfuscator api
        public static void Obfuscator(string path) {
            ObfuscatorDllByPath(path, true);
        }
        [MenuItem("Obfuscator/Start")]
        private static void ObfuscatorDll() {
            string path = EditorUtility.OpenFilePanel("", "", "*");
            obfuscatorDllByPath(path);
        }
        private static void obfuscatorDllByPath(string path) {
            ObfuscatorDllByPath(path, false);
        }
        private static void ObfuscatorDllByPath(string path, bool isBatchMode) {
            string folder = path.Replace(Path.GetFileName(path), "");
            if (File.Exists(path)) {
                DirectoryInfo info = new DirectoryInfo(Application.dataPath);
                HashSet<string> jsonNameList = new HashSet<string>();
                FindJsonClassInProject(info, jsonNameList);

                EditorUtility.ClearProgressBar();
                Obfuscator obfuscator = new Obfuscator(folder, true, true, false, true, true);

                #region exclude
                var itr = jsonNameList.GetEnumerator();
                while (itr.MoveNext()) {
                    obfuscator.ExcludeType(itr.Current);
                }
                itr.Dispose();

                //TODO
                //add the class use reflect, such as the classes inherit MonoBehavouir
                //the string just is partner of baseType's fullName
                obfuscator.ExcludeBase("UnityEngine");
                //obfuscator.CustomExcludeFun += you function
                //like just obfuscator you code namespace
                #endregion

                obfuscator.AddAssembly(path, true);
                if (!isBatchMode) {
                    obfuscator.Progress += obfuscator_NameObfuscated;
                }
                obfuscator.StartObfuscation();
            }
            if (!isBatchMode) {
                EditorUtility.ClearProgressBar();
            }
        }
        #endregion

        #region search json class
        private static void FindJsonClassInProject(DirectoryInfo dir, HashSet<string> result) {
            FileInfo[] files = dir.GetFiles("*.cs");

            FileAttributes fa;
            foreach (FileInfo item in files) {
                if (item.Extension != ".cs") {
                    continue;
                }
                //遍历时忽略隐藏文件
                fa = item.Attributes & FileAttributes.Hidden;
                if (fa != FileAttributes.Hidden) {
                    string[] lines = File.ReadAllLines(item.FullName);
                    string objectName;
                    foreach (var line in lines) {
                        if (MatchJsonObject(line, out objectName) && !result.Contains(objectName)) {
                            result.Add(objectName);
                        }
                    }
                }
            }

            DirectoryInfo[] dis = dir.GetDirectories();
            foreach (DirectoryInfo di in dis) {
                FindJsonClassInProject(di, result);
            }
        }
        private static bool MatchJsonObject(string text, out string value) {
            value = string.Empty;
            Regex reg = new Regex(@"(?<=JsonMapper.ToObject\<).+(?=\>)");
            Match mc = reg.Match(text);
            bool result = mc.Success;

            reg = new Regex(@"(?<=\<).+(?=\>)");
            while (mc.Success) {
                text = mc.Value;
                mc = reg.Match(text);
            }

            string[] list = text.Split(',');
            if (list.Length > 0) {
                text = list[list.Length - 1].Trim();
            }
            value = text;

            return result;
        }
        private static void obfuscator_NameObfuscated(string phaseName, int percents) {
            EditorUtility.DisplayProgressBar("混淆dll", phaseName, (float)percents / 100);
        }
        #endregion

    }
}
