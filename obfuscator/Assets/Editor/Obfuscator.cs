using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Mono.Cecil;

namespace SharpObfuscator.Obfuscation2 {
    public delegate void ObfuscatorOutputEvent(StreamReader standardOutput, StreamReader standardError);
    public delegate string ObfuscatorRequestReferencedAssemblyPath(string typeToLoad, string initialAssemblyPath);
    public delegate void ObfuscatorNameObfuscated(ObfuscationItem item, string initialName, string obfuscatedName, string notes);
    public delegate void ObfuscatorProgress(string phaseName, int percents);

    sealed public class Obfuscator {

        #region Variables
        public Func<TypeDefinition, bool> CustomExcludeFun;
        private Dictionary<string, bool> _assembliesPaths = new Dictionary<string, bool>();
        private List<string> _excludedTypesNames = new List<string>();
        private List<string> _excludedBaseTypePartner = new List<string>();
        private HashSet<string> m_excludeName = new HashSet<string>();

        private string _outputDirectory = "";

        private bool _obfuscateTypes;
        private bool _obfuscateMethods;
        private bool _obfuscateNamespaces;
        private bool _obfuscateProperties;
        private bool _obfuscateFields;
        TypeDefinitionCollection m_types;

        //----
        private XmlDocument _xmlMapping;
        private XmlElement _xmlMappingRoot;

        //----
        private long _obfuscatedMethodId = -1;
        private long _obfuscatedTypeId = -1;
        private long _obfuscatedNamespaceId = -1;
        private long _obfuscatedPropertyId = -1;
        private long _obfuscatedFieldId = -1;
        private float m_process = 0;

        private Dictionary<string, string> _resourcesMapping = new Dictionary<string, string>();

        private List<AssemblyDefinition> assembliesDefinitions = new List<AssemblyDefinition>();

        private Dictionary<string, string> _obfuscatedNamespaces = new Dictionary<string, string>();

        //---- Events
        //public event ObfuscatorOutputEvent OutputEvent;
        //public event ObfuscatorRequestReferencedAssemblyPath RequestReferencedAssemblyPath;
        //public event ObfuscatorNameObfuscated NameObfuscated;
        public event ObfuscatorProgress Progress;

        #endregion

        #region Constructor

        public Obfuscator(string outputDirectory, bool obfuscateTypes, bool obfuscateMethods,
            bool obfuscateNamespaces, bool obfuscateProperties, bool obfuscateMembers) {
            _outputDirectory = outputDirectory;
            _obfuscateTypes = obfuscateTypes;
            _obfuscateMethods = obfuscateMethods;
            _obfuscateNamespaces = obfuscateNamespaces;
            _obfuscateProperties = obfuscateProperties;
            _obfuscateFields = obfuscateMembers;
        }

        #endregion

        #region AddAssembly

        public void AddAssembly(string path, bool obfuscate) {
            _assembliesPaths.Add(path, obfuscate);
        }

        #endregion

        #region ExcludeType

        public void ExcludeType(string typeName) {
            _excludedTypesNames.Add(typeName);
        }
        public void ExcludeBase(string typePatner) {
            _excludedBaseTypePartner.Add(typePatner);
        }
        #endregion

        #region SetProgress

        private void SetProgress(string message, int percent) {
            if (Progress != null)
                Progress(message, percent);
        }

        #endregion

        #region StartObfuscation

        public void StartObfuscation() {
            AsyncStartObfuscation();
        }

        private void AsyncStartObfuscation() {
            List<string> assembliesPaths = new List<string>();
            List<bool> assembliesToObfuscate = new List<bool>();
            m_process = 0;

            SetProgress("Loading...", 0);

            //---- Create the Xml Document for mapping
            _xmlMapping = new XmlDocument();
            _xmlMappingRoot = _xmlMapping.CreateElement("mappings");
            _xmlMapping.AppendChild(_xmlMappingRoot);

            //---- Load the assemblies
            foreach (string assemblyPath in _assembliesPaths.Keys) {
                // Full load the assembly
                AssemblyDefinition assembly = AssemblyFactory.GetAssembly(assemblyPath);
                foreach (ModuleDefinition module in assembly.Modules)
                    module.FullLoad();

                assembliesDefinitions.Add(assembly);
                assembliesPaths.Add(Path.GetFileName(assemblyPath));
                assembliesToObfuscate.Add(_assembliesPaths[assemblyPath]);
            }

            SetProgress("Obfuscate...", 0);

            //---- Obfuscate the assemblies
            int assemblyIndex = -1;
            foreach (AssemblyDefinition assembly in assembliesDefinitions) {
                assemblyIndex++;

                if (!assembliesToObfuscate[assemblyIndex])
                    continue;

                SetProgress("Obfuscate assembly: " + assembly.Name.Name, 0);
                m_types = assembly.MainModule.Types;
                //---- Obfuscate Types / Methods
                int i = 0;
                int count = assembly.MainModule.Types.Count;
                //先查出excludeType 中的所有filed 和property的 fullName
                foreach (TypeDefinition type in m_types) {
                    RefToExcludeType(type);
                }

                foreach (TypeDefinition type in assembly.MainModule.Types) {
                    m_process = (float)i / (float)count;
                    ObfuscateType(type);
                    i++;
                }


                //---- Obfuscate Namespaces
                if (_obfuscateNamespaces)
                    foreach (TypeDefinition type in assembly.MainModule.Types)
                        ObfuscateNamespace(type);

                //---- Obfuscate Resources
                foreach (Resource resource in assembly.MainModule.Resources)
                    ObfuscateResource(resource);

                SetProgress("Obfuscate resource: " + assembly.Name.Name, 100);
            }

            SetProgress("Saving...", 0);

            //---- Save the modified assemblies
            assemblyIndex = -1;
            foreach (AssemblyDefinition assembly in assembliesDefinitions) {
                assemblyIndex++;

                //---- Create output directory if it doesn't exists
                if (Directory.Exists(_outputDirectory) == false)
                    Directory.CreateDirectory(_outputDirectory);

                //---- Delete previous file
                string outputFileName = Path.Combine(_outputDirectory, assembliesPaths[assemblyIndex]);
                if (File.Exists(outputFileName))
                    File.Delete(outputFileName);

                //---- Save the modified assembly
                AssemblyFactory.SaveAssembly(assembly, outputFileName);
            }

            //---- Save mapping
            _xmlMapping.Save(Path.Combine(_outputDirectory, "Mapping.xml"));

            SetProgress("Complete.", 100);
        }

        #endregion

        #region ObfuscateResource

        private void ObfuscateResource(Resource resource) {
            string resourceName = resource.Name.Substring(0, resource.Name.Length - 10);

            if (!_resourcesMapping.ContainsKey(resourceName))
                return;

            string obfucatedName = _resourcesMapping[resourceName];
            resource.Name = obfucatedName + ".resources";
        }

        #endregion

        #region ObfuscateNamespace

        private void ObfuscateNamespace(TypeDefinition type) {
            if (type.Namespace.Length < 1)
                return;

            if (ExcludeContainType(type))
                return;

            //---- Obfuscate
            string initialNamespace = type.Namespace;
            type.Namespace = GetObfuscatedNamespace(type.Namespace);

            //---- Update the type references in other assemblies
            foreach (AssemblyDefinition assembly in assembliesDefinitions)
                foreach (ModuleDefinition module in assembly.Modules)
                    foreach (TypeReference typeReference in module.TypeReferences)
                        if (typeReference.Namespace == initialNamespace)
                            typeReference.Namespace = type.Namespace;

            //---- Resources
            Dictionary<string, string> newDictionary = new Dictionary<string, string>();
            foreach (string key in _resourcesMapping.Keys) {
                string resValue = _resourcesMapping[key];
                if (resValue.Contains("."))
                    if (resValue.Substring(0, resValue.LastIndexOf('.')) == initialNamespace)
                        resValue = type.Namespace + resValue.Substring(resValue.LastIndexOf('.'));

                newDictionary.Add(key, resValue);
            }

            _resourcesMapping = newDictionary;
        }

        private string GetObfuscatedNamespace(string initialNamespace) {
            if (_obfuscatedNamespaces.ContainsKey(initialNamespace))
                return _obfuscatedNamespaces[initialNamespace];

            string[] namespaceSet = initialNamespace.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            string currentNs = "";
            string currentNsObfuscated = "";
            foreach (string ns in namespaceSet) {
                if (currentNs.Length > 0) {
                    currentNs += ".";
                    currentNsObfuscated += ".";
                }

                currentNs += ns;

                if (!_obfuscatedNamespaces.ContainsKey(currentNs)) {
                    _obfuscatedNamespaces.Add(currentNs, currentNsObfuscated + ObfuscateString(ObfuscationItem.Namespace, ns, ""));
                }

                currentNsObfuscated = _obfuscatedNamespaces[currentNs];
            }

            return _obfuscatedNamespaces[initialNamespace];
        }

        #endregion

        #region ObfuscateType

        private void ObfuscateType(TypeDefinition type) {

            if (type.Name == "<Module>")
                return;

            if (type.IsRuntimeSpecialName)
                return;

            if (type.IsSpecialName)
                return;

            if (type.Name.Contains("Resources"))
                return;

            if (type.Name.StartsWith("<")) // Like "<PrivateImplementationDetails>"
                return;

            //if (type.Name.Contains("__"))
            //	return;
            if (type.Name.Contains("`"))
                return;
            if (type.IsEnum) {
                return;
            }

            if (ExcludeContainType(type))
                return;


            //---- Obfuscate
            string initialTypeName = type.FullName;
            type.Name = ObfuscateString(ObfuscationItem.Type, type.Name, "");

            //---- Prepare ressources names
            if (!initialTypeName.Contains("/")) {
                // Save the obfuscation mapping
                _resourcesMapping.Add(initialTypeName, type.FullName);
            }

            //---- Update the type references in other assemblies
            foreach (AssemblyDefinition assembly in assembliesDefinitions)
                foreach (ModuleDefinition module in assembly.Modules)
                    foreach (TypeReference typeReference in module.TypeReferences)
                        if (typeReference.FullName == initialTypeName)
                            typeReference.Name = type.Name;

            //---- Obfuscate methods
            foreach (MethodDefinition method in type.Methods)
                ObfuscateMethod(type, initialTypeName, method);

            //---- Obfuscate properties
            foreach (PropertyDefinition property in type.Properties)
                ObfuscateProperty(type, property);

            //---- Obfuscate fields
            foreach (FieldDefinition field in type.Fields)
                ObfuscateField(type, field);
        }
        private bool ExcludeContainType(TypeDefinition type) {
            if ((type.Attributes & Mono.Cecil.TypeAttributes.Serializable) != 0) {
                return true;
            }
            string fullName = type.FullName;
            foreach (var name in _excludedBaseTypePartner) {
                if (IsBaseOnTargetStr(type, name)) {
                    return true;
                }
            }
            foreach (var name in _excludedTypesNames) {
                if (fullName.Contains(name)) {
                    return true;
                }
            }
            foreach (var name in m_excludeName) {
                if (name.Contains(fullName)) {
                    return true;
                }
            }
            if (CustomExcludeFun != null) {
                return CustomExcludeFun(type);
            }
            return false;
        }
        private void RefToExcludeType(TypeDefinition type) {
            string fullName = type.FullName;
            bool isContain = false;
            foreach (var name in _excludedTypesNames) {
                if (fullName.Contains(name)) {
                    isContain = true;
                    break;
                }
            }
            if (!isContain) {
                return;
            }

            //---- Obfuscate properties
            foreach (PropertyDefinition property in type.Properties) {
                string pName = property.PropertyType.FullName;
                if (!m_excludeName.Contains(pName)) {
                    m_excludeName.Add(pName);
                }
            }

            //---- Obfuscate fields
            foreach (FieldDefinition field in type.Fields) {
                string pName = field.FieldType.FullName;
                if (!m_excludeName.Contains(pName)) {
                    m_excludeName.Add(pName);
                }
            }
        }

        private bool IsBaseOnTargetStr(TypeDefinition type, string name) {
            if (type != null) {
                string fullName = type.FullName;
                if (fullName.Contains(name)) {
                    return true;
                }
                else if (type.BaseType != null) {
                    if (type.BaseType.FullName.Contains(name)) {
                        return true;
                    }
                    type = m_types[type.BaseType.FullName];
                    return IsBaseOnTargetStr(type, name);
                }
            }
            return false;
        }
        #endregion

        #region ObfuscateMethod

        private void ObfuscateMethod(TypeDefinition type, string initialTypeName, MethodDefinition method) {
            if (method.IsConstructor)
                return;

            if (method.IsRuntime)
                return;

            if (method.IsRuntimeSpecialName)
                return;

            if (method.IsSpecialName)
                return;

            if (method.IsVirtual)
                return;

            if (method.IsAbstract)
                return;
            if (method.GenericParameters != null && method.GenericParameters.Count > 0) {
                return;
            }

            if (method.Overrides.Count > 0)
                return;

            if (method.Name.StartsWith("<"))
                return;

            string initialName = method.Name;
            string obfuscatedName = ObfuscateString(ObfuscationItem.Method, method.Name, "");

            //---- Update the type references in other assemblies
            foreach (MethodReference reference in MethodReference.AllReferences) {
                if (reference.DeclaringType.Name == type.Name &&
                    reference.DeclaringType.Namespace == type.Namespace)
                    if (!Object.ReferenceEquals(reference, method) &&
                        reference.Name == initialName &&
                        reference.HasThis == method.HasThis &&
                        reference.CallingConvention == method.CallingConvention &&
                        reference.Parameters.Count == method.Parameters.Count &&
                        reference.GenericParameters.Count == method.GenericParameters.Count &&
                        reference.ReturnType.ReturnType.Name == method.ReturnType.ReturnType.Name &&
                        reference.ReturnType.ReturnType.Namespace == method.ReturnType.ReturnType.Namespace
                        ) {
                        bool paramsEquals = true;
                        for (int paramIndex = 0; paramIndex < method.Parameters.Count; paramIndex++)
                            if (reference.Parameters[paramIndex].ParameterType.FullName != method.Parameters[paramIndex].ParameterType.FullName) {
                                paramsEquals = false;
                                break;
                            }

                        for (int paramIndex = 0; paramIndex < method.GenericParameters.Count; paramIndex++)
                            if (reference.GenericParameters[paramIndex].FullName != method.GenericParameters[paramIndex].FullName) {
                                paramsEquals = false;
                                break;
                            }

                        try {
                            if (paramsEquals)
                                reference.Name = obfuscatedName;
                        }
                        catch (InvalidOperationException) { }
                    }

            }

            method.Name = obfuscatedName;
        }

        #endregion

        #region ObfuscateProperty

        /// <summary>
        /// TODO:
        /// * Skip special properties: indexers, > < etc..
        /// </summary>
        /// <param name="type"></param>
        /// <param name="initialTypeName"></param>
        /// <param name="property"></param>
        private void ObfuscateProperty(TypeDefinition type, PropertyDefinition property) {
            if (property.IsSpecialName)
                return;

            if (property.IsRuntimeSpecialName)
                return;

            string obfuscatedName = ObfuscateString(ObfuscationItem.Property, property.Name, "");

            //---- Update the type references in other assemblies
            foreach (MethodReference reference in MethodReference.AllReferences) {
                if (reference.DeclaringType.Name == type.Name &&
                    reference.DeclaringType.Namespace == type.Namespace) {
                    if (!Object.ReferenceEquals(reference, property) &&
                        (reference.Name == property.Name) &&
                        (reference.Parameters.Count == property.Parameters.Count)
                        ) {
                        bool paramsEquals = true;
                        for (int paramIndex = 0; paramIndex < property.Parameters.Count; paramIndex++)
                            if (reference.Parameters[paramIndex].ParameterType.FullName != property.Parameters[paramIndex].ParameterType.FullName) {
                                paramsEquals = false;
                                break;
                            }

                        try {
                            if (paramsEquals)
                                reference.Name = obfuscatedName;
                        }
                        catch (InvalidOperationException) { }
                    }
                }
            }

            property.Name = obfuscatedName;
        }

        #endregion

        #region ObfuscateFields

        private void ObfuscateField(TypeDefinition type, FieldDefinition field) {
            if (field.IsRuntimeSpecialName)
                return;

            if (field.IsSpecialName)
                return;

            if (field.Name.StartsWith("<"))
                return;

            string initialName = field.Name;
            string obfuscatedName = ObfuscateString(ObfuscationItem.Field, field.Name, "");

            //---- Update the type references in other assemblies
            foreach (MethodReference reference in MethodReference.AllReferences) {
                if (reference.DeclaringType.Name == type.Name &&
                    reference.DeclaringType.Namespace == type.Namespace)
                    if (!Object.ReferenceEquals(reference, field) &&
                        (reference.Name == initialName)
                        ) {
                        try {
                            reference.Name = obfuscatedName;
                        }
                        catch (InvalidOperationException) { }
                    }

            }

            field.Name = obfuscatedName;
        }

        #endregion

        #region ObfuscateString
        internal string ObfuscateString(ObfuscationItem item, string initialName, string notes) {
            string obfuscated = null;

            switch (item) {
                case ObfuscationItem.Method:
                    if (!_obfuscateMethods)
                        return initialName;
                    _obfuscatedMethodId++;
                    obfuscated = "M" + _obfuscatedMethodId;
                    break;

                case ObfuscationItem.Type:
                    if (!_obfuscateTypes)
                        return initialName;
                    _obfuscatedTypeId++;
                    obfuscated = "A" + _obfuscatedTypeId;
                    break;

                case ObfuscationItem.Namespace:
                    _obfuscatedNamespaceId++;
                    obfuscated = "N" + _obfuscatedNamespaceId;
                    break;

                case ObfuscationItem.Property:
                    if (!_obfuscateProperties)
                        return initialName;
                    _obfuscatedPropertyId++;
                    obfuscated = "P" + _obfuscatedPropertyId;
                    break;

                case ObfuscationItem.Field:
                    if (!_obfuscateFields)
                        return initialName;
                    _obfuscatedFieldId++;
                    obfuscated = "F" + _obfuscatedFieldId;
                    break;
            }

            if (Progress != null)
                Progress(string.Format("{0} to {1}, notes {2}", initialName, obfuscated, notes), (int)(m_process * 100));

            // Xml mapping document
            XmlElement mappingElement = _xmlMapping.CreateElement("mapping");
            _xmlMappingRoot.AppendChild(mappingElement);
            mappingElement.SetAttribute("Type", item.ToString());
            mappingElement.SetAttribute("InitialValue", initialName);
            mappingElement.SetAttribute("ObfuscatedValue", obfuscated);

            return obfuscated;
        }

        #endregion

    }

    #region ObfuscationItem

    public enum ObfuscationItem {
        Namespace,
        Type,
        Method,
        Property,
        Field
    }

    #endregion

}