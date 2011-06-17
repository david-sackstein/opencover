﻿//
// OpenCover - S Wilde
//
// This source code is released under the MIT License; see the accompanying license file.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Pdb;
using OpenCover.Framework.Model;
using File = OpenCover.Framework.Model.File;

namespace OpenCover.Framework.Symbols
{
    public class CecilSymbolManager : ISymbolManager
    {
        private const int stepOverLineCode = 0xFEEFEE;
        private readonly ICommandLine _commandLine;
        private string _modulePath;
        private AssemblyDefinition _sourceAssembly;

        public CecilSymbolManager(ICommandLine commandLine)
        {
            _commandLine = commandLine;
        }

        public string ModulePath
        {
            get { return _modulePath; }
        }

        public void Initialise(string modulePath)
        {
            _modulePath = modulePath;
        }

        private AssemblyDefinition SourceAssembly
        {
            get
            {
                if (_sourceAssembly==null)
                {
                    var resolver = new DefaultAssemblyResolver();
                    if (string.IsNullOrEmpty(Path.GetDirectoryName(_modulePath)) == false)
                    {
                        resolver.AddSearchDirectory(Path.GetDirectoryName(_modulePath));
                        if (!string.IsNullOrEmpty(_commandLine.TargetDir))
                        {
                            resolver.AddSearchDirectory(_commandLine.TargetDir);
                        }
                    }

                    var parameters = new ReaderParameters
                    {
                        SymbolReaderProvider = new PdbReaderProvider(),
                        ReadingMode = ReadingMode.Immediate,
                        AssemblyResolver = resolver,
                    };

                    _sourceAssembly = AssemblyDefinition.ReadAssembly(_modulePath, parameters);
                    _sourceAssembly.MainModule.ReadSymbols();
                }
                return _sourceAssembly;
            }
        }



        public File[] GetFiles()
        {
            var list = new List<File>();
            foreach (var instrumentableType in GetInstrumentableTypes())
            {
                list.AddRange(instrumentableType.Files);
            }
            return list.Distinct(new FileEqualityComparer()).Select(file => file).ToArray();
        }

        public Class[] GetInstrumentableTypes()
        {
            var classes = new List<Class>();
            IEnumerable<TypeDefinition> typeDefinitions = SourceAssembly.MainModule.Types;
            GetInstrumentableTypes(typeDefinitions, classes);
            return classes.ToArray();
        }

        private static void GetInstrumentableTypes(IEnumerable<TypeDefinition> typeDefinitions, List<Class> classes)
        {
            foreach (var typeDefinition in typeDefinitions)
            {
                if (typeDefinition.IsInterface && typeDefinition.IsAbstract) continue;
                var @class = new Class() {FullName = typeDefinition.FullName};
                var list = new List<string>();
                foreach (var methodDefinition in typeDefinition.Methods)
                {
                    if (methodDefinition.Body != null && methodDefinition.Body.Instructions != null)
                    {
                        foreach (var instruction in methodDefinition.Body.Instructions)
                        {
                            if (instruction.SequencePoint != null)
                            {
                                list.Add(instruction.SequencePoint.Document.Url);
                                break;
                            }
                        }
                    }
                }
                @class.Files = list.Distinct().Select(file => new File { FullPath = file }).ToArray();
                classes.Add(@class);
                if (typeDefinition.HasNestedTypes) GetInstrumentableTypes(typeDefinition.NestedTypes, classes); 
            }
        }

        public Method[] GetConstructorsForType(Class type, File[] files)
        {
            var methods = new List<Method>();
            IEnumerable<TypeDefinition> typeDefinitions = SourceAssembly.MainModule.Types;
            GetConstructorsForType(typeDefinitions, type, methods, files);
            return methods.ToArray();
        }

        private static void GetConstructorsForType(IEnumerable<TypeDefinition> typeDefinitions, Class type, List<Method> methods, File[] files)
        {
            foreach (var typeDefinition in typeDefinitions)
            {
                if (typeDefinition.FullName == type.FullName)
                {
                    foreach (var methodDefinition in typeDefinition.Methods)
                    {
                        if (methodDefinition.IsConstructor)
                        {
                            var method = new Method() { Name = methodDefinition.FullName, MetadataToken = methodDefinition.MetadataToken.ToInt32()};
                            var definition = methodDefinition;
                            method.FileRef = files.Where(x => x.FullPath == GetFirstFile(definition)).Select(x => new FileRef() { UniqueId = x.UniqueId }).FirstOrDefault();
                            methods.Add(method);
                        }
                    }
                }
                if (typeDefinition.HasNestedTypes) GetConstructorsForType(typeDefinition.NestedTypes, type, methods, files); 
            }
        }

        public Method[] GetMethodsForType(Class type, File[] files)
        {
            var methods = new List<Method>();
            IEnumerable<TypeDefinition> typeDefinitions = SourceAssembly.MainModule.Types;
            GetMethodsForType(typeDefinitions, type, methods, files);
            return methods.ToArray();
        }

        private static string GetFirstFile(MethodDefinition definition)
        {
            if (definition.HasBody && definition.Body.Instructions!=null)
            {
                var filePath = definition.Body.Instructions
                    .Where(x => x.SequencePoint != null && x.SequencePoint.Document != null && x.SequencePoint.StartLine != stepOverLineCode)
                    .Select(x => x.SequencePoint.Document.Url)
                    .FirstOrDefault();
                return filePath;
            }
            return null;
        }

        private static void GetMethodsForType(IEnumerable<TypeDefinition> typeDefinitions, Class type, List<Method> methods, File[] files)
        {
            foreach (var typeDefinition in typeDefinitions)
            {
                if (typeDefinition.FullName == type.FullName)
                {
                    foreach (var methodDefinition in typeDefinition.Methods)
                    {
                        if (!methodDefinition.IsConstructor)
                        {
                            var method = new Method() { Name = methodDefinition.FullName, MetadataToken = methodDefinition.MetadataToken.ToInt32() };
                            var definition = methodDefinition;
                            method.FileRef = files.Where(x => x.FullPath == GetFirstFile(definition)).Select(x => new FileRef() {UniqueId = x.UniqueId}).FirstOrDefault();
                            methods.Add(method);
                        }
                    }
                }
                if (typeDefinition.HasNestedTypes) GetMethodsForType(typeDefinition.NestedTypes, type, methods, files);
            }
        }

        public SequencePoint[] GetSequencePointsForToken(int token)
        {
            var list = new List<SequencePoint>();
            IEnumerable<TypeDefinition> typeDefinitions = SourceAssembly.MainModule.Types;
            GetSequencePointsForToken(typeDefinitions, token, list);
            return list.ToArray();
        }

        private static void GetSequencePointsForToken(IEnumerable<TypeDefinition> typeDefinitions, int token, List<SequencePoint> list)
        {
            foreach (var typeDefinition in typeDefinitions)
            {
                foreach (var methodDefinition in typeDefinition.Methods)
                {
                    if (methodDefinition.MetadataToken.ToInt32() == token)
                    {
                        if (methodDefinition.Body != null && methodDefinition.Body.Instructions != null)
                        {
                            UInt32 ordinal = 0;
                            foreach (var instruction in methodDefinition.Body.Instructions)
                            {
                                if (instruction.SequencePoint != null && instruction.SequencePoint.StartLine != stepOverLineCode)
                                {
                                    var sp = instruction.SequencePoint;
                                    var point = new SequencePoint()
                                                    {
                                                        EndColumn = sp.EndColumn,
                                                        EndLine = sp.EndLine,
                                                        Offset = instruction.Offset,
                                                        Ordinal = ordinal++,
                                                        StartColumn = sp.StartColumn,
                                                        StartLine = sp.StartLine,
                                                    };
                                    list.Add(point);
                                }
                            }
                        }   
                    }
                }
                if (typeDefinition.HasNestedTypes) GetSequencePointsForToken(typeDefinition.NestedTypes, token, list);
            }
        }
    }
}