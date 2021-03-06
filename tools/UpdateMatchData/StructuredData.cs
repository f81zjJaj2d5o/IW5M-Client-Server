﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace UpdateMatchData
{
    public class StructuredDataValue
    {
        private object _object;

        public StructuredDataValue(object value)
        {
            _object = value;
        }

        public T Get<T>()
        {
            return (T)Convert.ChangeType(_object, typeof(T));
        }
    }

    public class StructuredData
    {
        private int _version;
        private Dictionary<string, Dictionary<string, StructItemDef>> _structs;
        private Dictionary<string, Dictionary<string, int>> _enums;
        private Dictionary<string, StructEnumArray> _enumArrays;
        //private List<StructuredDataEnumArray> _enumArrays;

        private byte[] _data;

        private class StructuredDataElement
        {
            public string Type { get; set; }
            public int Offset { get; set; }

            public int Length { get; set; }
            public string ChildType { get; set; }
            public int ChildSize { get; set; }
            public int Bit { get; set; }

            public void ResetData()
            {
                Length = 0;
                ChildType = null;
                ChildSize = 0;
                Bit = 0;
            }
        }

        public StructuredData(string definitionFile)
        {
            LoadDefinition(definitionFile);
        }

        public void SetData(byte[] data)
        {
            _data = data;

            //ValidateVersion();
        }

        public StructuredDataValue Get(string path)
        {
            var elements = path.Split('.');
            var element = Trace(elements);

            return ReadItem(element);
        }

        private StructuredDataValue ReadItem(StructuredDataElement element)
        {
            var offset = element.Offset;// +4;
            object item = null;

            switch (GetActualType(element.Type))
            {
                case "int":
                    item = ReadInt32(offset);
                    break;
                case "short":
                    item = ReadInt16(offset);
                    break;
                case "byte":
                    item = ReadInt8(offset);
                    break;
                case "float":
                    item = ReadFloat(offset);
                    break;
                case "enum":
                    var value = ReadInt16(offset);

                    foreach (var enumItem in _enums[element.Type])
                    {
                        if (enumItem.Value == value)
                        {
                            item = enumItem.Key;
                            break;
                        }
                    }

                    break;
                case "string":
                    item = ReadString(offset, element.Length);
                    break;
                case "bool":
                    var bvalue = ReadInt8(offset);

                    if (element.Bit > 0)
                    {
                        bvalue >>= element.Bit;
                        bvalue &= 1;
                    }

                    item = (bvalue == 1) ? true : false;
                    break;
            }

            return new StructuredDataValue(item);
        }

        private int ReadInt32(int offset)
        {
            return BitConverter.ToInt32(_data, offset);
        }

        private short ReadInt16(int offset)
        {
            return BitConverter.ToInt16(_data, offset);
        }

        private byte ReadInt8(int offset)
        {
            return _data[offset];
        }

        private float ReadFloat(int offset)
        {
            return BitConverter.ToSingle(_data, offset);
        }

        private string ReadString(int offset, int length)
        {
            return Encoding.ASCII.GetString(_data, offset, length).Split('\0')[0];
        }

        private StructuredDataElement Trace(string[] path)
        {
            var element = new StructuredDataElement();
            element.Type = "playerdata";
            element.Offset = 0;

            foreach (var name in path)
            {
                switch (GetActualType(element.Type))
                {
                    case "struct":
                        element = GetStructChild(element, name);
                        break;
                    case "indexedarr":
                        element = GetArrayIndex(element, name);
                        break;
                    case "enumarr":
                        element = GetArrayEnum(element, name);
                        break;
                }
            }

            return element;
        }

        private StructuredDataElement GetArrayEnum(StructuredDataElement item, string name)
        {
            var found = false;
            var enumArray = _enumArrays[item.Type];
            var enumeration = _enums[enumArray.Enum];

            foreach (var enumItem in enumeration)
            {
                if (enumItem.Key == name)
                {
                    var index = enumItem.Value;

                    item.ResetData();
                    item.Type = enumArray.Type;

                    if (item.Type == "bool")
                    {
                        item.Offset += (index / 8); // should be floored
                        item.Bit = (index % 8);
                    }
                    else
                    {
                        item.Offset += (index * enumArray.Size);
                    }

                    found = true;
                }
            }

            if (!found)
            {
                throw new KeyNotFoundException("Could not find any such key in the specified enum.");
            }

            return item;
        }

        private StructuredDataElement GetArrayIndex(StructuredDataElement item, string name)
        {
            var index = int.Parse(name);

            if (index >= item.Length)
            {
                throw new IndexOutOfRangeException("Index is outside of the indexedarr's bounds.");
            }

            var childSize = item.ChildSize;
            item.Type = item.ChildType;

            item.ResetData();

            if (item.Type == "bool")
            {
                item.Offset += (index / 8); // should be floored
                item.Bit = (index % 8);
            }
            else
            {
                item.Offset += (index * childSize);
            }

            return item;
        }

        private StructuredDataElement GetStructChild(StructuredDataElement item, string name)
        {
            var found = false;
            var structure = _structs[item.Type];

            foreach (var element in structure)
            {
                if (element.Key == name)
                {
                    item.Type = element.Value.Type;
                    item.Offset += element.Value.Offset;

                    item.ResetData();

                    if (element.Value.Length > 0)
                    {
                        item.Length = element.Value.Length;
                    }

                    if (element.Value.ChildType != null)
                    {
                        item.ChildType = element.Value.ChildType;
                        item.ChildSize = element.Value.ChildSize;
                    }

                    found = true;
                }
            }

            if (!found)
            {
                throw new KeyNotFoundException("Could not find any such key in the specified struct.");
            }

            return item;
        }

        private string GetActualType(string type)
        {
            if (_structs.ContainsKey(type))
            {
                return "struct";
            }
            else if (_enumArrays.ContainsKey(type))
            {
                return "enumarr";
            }
            else if (_enums.ContainsKey(type))
            {
                return "enum";
            }
            else
            {
                return type;
            }
        }

        private void LoadDefinition(string filename)
        {
            var document = XDocument.Load(filename);

            _version = int.Parse(document.Root.Attribute("version").Value);
            _enums = LoadEnums(document);
            _structs = LoadStructs(document);
            _enumArrays = LoadEnumArrays(document);
        }

        private class StructItemDef
        {
            public string Type { get; set; }
            public string Name { get; set; }
            public int Offset { get; set; }
            public int Length { get; set; }
            public string ChildType { get; set; }
            public int ChildSize { get; set; }
        }

        private class StructEnumArray
        {
            public string Enum { get; set; }
            public string Type { get; set; }
            public int Size { get; set; }
        }

        private Dictionary<string, Dictionary<string, StructItemDef>> LoadStructs(XDocument document)
        {
            var structs = new Dictionary<string, Dictionary<string, StructItemDef>>();

            var documentStructs = from structure in document.Descendants("structs").First().Descendants("struct")
                                  select new
                                  {
                                      Name = structure.Attribute("name").Value,
                                      Items = from item in structure.Elements()
                                              select new
                                              {
                                                  Type = item.Name.LocalName,
                                                  Name = item.Attribute("name").Value,
                                                  Offset = int.Parse(item.Attribute("offset").Value),
                                                  Node = item
                                              }
                                  };

            foreach (var structure in documentStructs)
            {
                var structureItems = new Dictionary<string, StructItemDef>();

                foreach (var item in structure.Items)
                {
                    var structItem = new StructItemDef()
                    {
                        Name = item.Name,
                        Offset = item.Offset,
                        Type = item.Type
                    };

                    if (item.Type == "string" || item.Type == "indexedarr")
                    {
                        structItem.Length = int.Parse(item.Node.Attribute("length").Value);
                    }

                    var descendants = item.Node.Descendants();

                    if (descendants.Count() > 0)
                    {
                        var descendant = descendants.First();
                        structItem.ChildType = descendant.Name.LocalName;
                        structItem.ChildSize = int.Parse(descendant.Attribute("size").Value);
                    }

                    structureItems.Add(structItem.Name, structItem);
                }

                structs.Add(structure.Name, structureItems);
            }

            return structs;
        }

        private Dictionary<string, Dictionary<string, int>> LoadEnums(XDocument document)
        {
            var enums = new Dictionary<string, Dictionary<string, int>>();

            var documentEnums = from enumeration in document.Descendants("enums").First().Descendants("enum")
                                select new
                                {
                                    Name = enumeration.Attribute("name").Value,
                                    Items = from item in enumeration.Descendants("index")
                                            select new
                                            {
                                                Name = item.Attribute("name").Value,
                                                Index = int.Parse(item.Attribute("index").Value)
                                            }
                                };

            foreach (var enumeration in documentEnums)
            {
                var enumerationItems = new Dictionary<string, int>();

                foreach (var item in enumeration.Items)
                {
                    enumerationItems.Add(item.Name, item.Index);
                }

                enums.Add(enumeration.Name, enumerationItems);
            }

            return enums;
        }

        private Dictionary<string, StructEnumArray> LoadEnumArrays(XDocument document)
        {
            var enumArrays = new Dictionary<string, StructEnumArray>();

            var documentEnumArrays = from enumArray in document.Descendants("enumarrays").First().Descendants("enumarray")
                                     select new
                                     {
                                         Name = enumArray.Attribute("name").Value,
                                         Enum = enumArray.Attribute("enum").Value,
                                         Type = enumArray.Descendants().First().Name.LocalName,
                                         Size = int.Parse(enumArray.Descendants().First().Attribute("size").Value)
                                     };

            foreach (var enumArray in documentEnumArrays)
            {
                enumArrays.Add(enumArray.Name, new StructEnumArray()
                {
                    Enum = enumArray.Enum,
                    Size = enumArray.Size,
                    Type = enumArray.Type
                });
            }

            return enumArrays;
        }
    }

}
