using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;

namespace AssetStudio
{
    public static class TypeTreeHelper
    {
        public static string ReadTypeString(TypeTree m_Type, ObjectReader reader)
        {
            // Dynamic managed data cannot be rendered by the static-node-only
            // dump walker. Use the same exact reader as JSON/reference export.
            if(m_Type.m_Nodes.Any(n=>n.m_Type=="ManagedReferencesRegistry"))
                return Newtonsoft.Json.JsonConvert.SerializeObject(ReadType(m_Type,reader),Newtonsoft.Json.Formatting.Indented);
            reader.Reset();
            var sb = new StringBuilder();
            var m_Nodes = m_Type.m_Nodes;
            for (int i = 0; i < m_Nodes.Count; i++)
            {
                ReadStringValue(sb, m_Nodes, reader, ref i);
            }
            var readed = reader.Position - reader.byteStart;
            if (readed != reader.byteSize)
            {
                Logger.Info($"Error while read type, read {readed} bytes but expected {reader.byteSize} bytes");
            }
            return sb.ToString();
        }

        private static void ReadStringValue(StringBuilder sb, List<TypeTreeNode> m_Nodes, EndianBinaryReader reader, ref int i)
        {
            var m_Node = m_Nodes[i];
            var level = m_Node.m_Level;
            var varTypeStr = m_Node.m_Type;
            var varNameStr = m_Node.m_Name;
            object value = null;
            var append = true;
            var align = (m_Node.m_MetaFlag & 0x4000) != 0;
            switch (varTypeStr)
            {
                case "SInt8":
                    value = reader.ReadSByte();
                    break;
                case "UInt8":
                    value = reader.ReadByte();
                    break;
                case "char":
                    value = m_Node.m_ByteSize==1?(char)reader.ReadByte():BitConverter.ToChar(reader.ReadBytes(2), 0);
                    break;
                case "short":
                case "SInt16":
                    value = reader.ReadInt16();
                    break;
                case "UInt16":
                case "unsigned short":
                    value = reader.ReadUInt16();
                    break;
                case "int":
                case "SInt32":
                    value = reader.ReadInt32();
                    break;
                case "UInt32":
                case "unsigned int":
                case "Type*":
                    value = reader.ReadUInt32();
                    break;
                case "long long":
                case "SInt64":
                    value = reader.ReadInt64();
                    break;
                case "UInt64":
                case "unsigned long long":
                case "FileSize":
                    value = reader.ReadUInt64();
                    break;
                case "float":
                    value = reader.ReadSingle();
                    break;
                case "double":
                    value = reader.ReadDouble();
                    break;
                case "bool":
                    value = reader.ReadBoolean();
                    break;
                case "string":
                    append = false;
                    var str = reader.ReadAlignedString();
                    sb.AppendFormat("{0}{1} {2} = \"{3}\"\r\n", (new string('\t', level)), varTypeStr, varNameStr, str);
                    var toSkip = GetNodes(m_Nodes, i);
                    i += toSkip.Count - 1;
                    break;
                case "map":
                    {
                        if ((m_Nodes[i + 1].m_MetaFlag & 0x4000) != 0)
                            align = true;
                        append = false;
                        sb.AppendFormat("{0}{1} {2}\r\n", (new string('\t', level)), varTypeStr, varNameStr);
                        sb.AppendFormat("{0}{1} {2}\r\n", (new string('\t', level + 1)), "Array", "Array");
                        var size = reader.ReadInt32();
                        sb.AppendFormat("{0}{1} {2} = {3}\r\n", (new string('\t', level + 1)), "int", "size", size);
                        var map = GetNodes(m_Nodes, i);
                        i += map.Count - 1;
                        var first = GetNodes(map, 4);
                        var next = 4 + first.Count;
                        var second = GetNodes(map, next);
                        for (int j = 0; j < size; j++)
                        {
                            sb.AppendFormat("{0}[{1}]\r\n", (new string('\t', level + 2)), j);
                            sb.AppendFormat("{0}{1} {2}\r\n", (new string('\t', level + 2)), "pair", "data");
                            int tmp1 = 0;
                            int tmp2 = 0;
                            ReadStringValue(sb, first, reader, ref tmp1);
                            ReadStringValue(sb, second, reader, ref tmp2);
                        }
                        break;
                    }
                case "TypelessData":
                    {
                        append = false;
                        var size = reader.ReadInt32();
                        reader.ReadBytes(size);
                        i += 2;
                        sb.AppendFormat("{0}{1} {2}\r\n", (new string('\t', level)), varTypeStr, varNameStr);
                        sb.AppendFormat("{0}{1} {2} = {3}\r\n", (new string('\t', level)), "int", "size", size);
                        break;
                    }
                default:
                    {
                        if (i < m_Nodes.Count - 1 && m_Nodes[i + 1].m_Type == "Array") //Array
                        {
                            if ((m_Nodes[i + 1].m_MetaFlag & 0x4000) != 0)
                                align = true;
                            append = false;
                            sb.AppendFormat("{0}{1} {2}\r\n", (new string('\t', level)), varTypeStr, varNameStr);
                            sb.AppendFormat("{0}{1} {2}\r\n", (new string('\t', level + 1)), "Array", "Array");
                            var size = reader.ReadInt32();
                            sb.AppendFormat("{0}{1} {2} = {3}\r\n", (new string('\t', level + 1)), "int", "size", size);
                            var vector = GetNodes(m_Nodes, i);
                            i += vector.Count - 1;
                            for (int j = 0; j < size; j++)
                            {
                                sb.AppendFormat("{0}[{1}]\r\n", (new string('\t', level + 2)), j);
                                int tmp = 3;
                                ReadStringValue(sb, vector, reader, ref tmp);
                            }
                            break;
                        }
                        else //Class
                        {
                            append = false;
                            sb.AppendFormat("{0}{1} {2}\r\n", (new string('\t', level)), varTypeStr, varNameStr);
                            var @class = GetNodes(m_Nodes, i);
                            i += @class.Count - 1;
                            for (int j = 1; j < @class.Count; j++)
                            {
                                ReadStringValue(sb, @class, reader, ref j);
                            }
                            break;
                        }
                    }
            }
            if (append)
                sb.AppendFormat("{0}{1} {2} = {3}\r\n", (new string('\t', level)), varTypeStr, varNameStr, value);
            if (align)
                reader.AlignStream();
        }

        public static OrderedDictionary ReadType(TypeTree m_Types, ObjectReader reader, Action<string,int,long,long> reference = null, Action<string,string,long> stringField = null)
        {
            reader.Reset();
            var obj = new OrderedDictionary();
            var m_Nodes = m_Types.m_Nodes;
            for (int i = 1; i < m_Nodes.Count; i++)
            {
                var m_Node = m_Nodes[i];
                var varNameStr = m_Node.m_Name;
                obj[varNameStr] = ReadValue(m_Nodes, reader, ref i, varNameStr, reference, stringField);
            }
            var readed = reader.Position - reader.byteStart;
            if (readed != reader.byteSize)
            {
                Logger.Info($"Error while read type, read {readed} bytes but expected {reader.byteSize} bytes");
            }
            return obj;
        }

        private static object ReadValue(List<TypeTreeNode> m_Nodes, EndianBinaryReader reader, ref int i, string field="", Action<string,int,long,long> reference=null, Action<string,string,long> stringField=null, bool inRegistry=false, int depth=0)
        {
            if(depth>256)throw new InvalidDataException("Type tree managed/class nesting limit at "+field);
            long fieldOffset=reader.Position;
            var m_Node = m_Nodes[i];
            var varTypeStr = m_Node.m_Type;
            if(varTypeStr=="ManagedReferencesRegistry")
            {
                // A host serializes one registry. Dynamic types can carry the
                // same schema node, but do not serialize another nested table.
                if(inRegistry){i+=GetNodes(m_Nodes,i).Count-1;return null;}
                inRegistry=true;
            }
            Logger.Verbose($"Reading {m_Node.m_Name} of type {varTypeStr}");
            object value;
            var align = (m_Node.m_MetaFlag & 0x4000) != 0;
            switch (varTypeStr)
            {
                case "SInt8":
                    value = reader.ReadSByte();
                    break;
                case "UInt8":
                    value = reader.ReadByte();
                    break;
                case "char":
                    value = m_Node.m_ByteSize==1?(char)reader.ReadByte():BitConverter.ToChar(reader.ReadBytes(2), 0);
                    break;
                case "short":
                case "SInt16":
                    value = reader.ReadInt16();
                    break;
                case "UInt16":
                case "unsigned short":
                    value = reader.ReadUInt16();
                    break;
                case "int":
                case "SInt32":
                    value = reader.ReadInt32();
                    break;
                case "UInt32":
                case "unsigned int":
                case "Type*":
                    value = reader.ReadUInt32();
                    break;
                case "long long":
                case "SInt64":
                    value = reader.ReadInt64();
                    break;
                case "UInt64":
                case "unsigned long long":
                case "FileSize":
                    value = reader.ReadUInt64();
                    break;
                case "float":
                    value = reader.ReadSingle();
                    break;
                case "double":
                    value = reader.ReadDouble();
                    break;
                case "bool":
                    value = reader.ReadBoolean();
                    break;
                case "string":
                    value = reader.ReadAlignedString();
                    stringField?.Invoke(field,(string)value,fieldOffset);
                    var toSkip = GetNodes(m_Nodes, i);
                    i += toSkip.Count - 1;
                    break;
                case "map":
                    {
                        if ((m_Nodes[i + 1].m_MetaFlag & 0x4000) != 0)
                            align = true;
                        var map = GetNodes(m_Nodes, i);
                        i += map.Count - 1;
                        var first = GetNodes(map, 4);
                        var next = 4 + first.Count;
                        var second = GetNodes(map, next);
                        var size = reader.ReadInt32();
                        if(size<0)throw new InvalidDataException("Negative map count at "+field);
                        var dic = new List<KeyValuePair<object, object>>();
                        for (int j = 0; j < size; j++)
                        {
                            int tmp1 = 0;
                            int tmp2 = 0;
                            dic.Add(new KeyValuePair<object, object>(ReadValue(first, reader, ref tmp1,field+"["+j+"].first",reference,stringField,inRegistry,depth+1), ReadValue(second, reader, ref tmp2,field+"["+j+"].second",reference,stringField,inRegistry,depth+1)));
                        }
                        value = dic;
                        break;
                    }
                case "TypelessData":
                    {
                        var size = reader.ReadInt32();
                        value = reader.ReadBytes(size);
                        i += 2;
                        break;
                    }
                default:
                    {
                        if (i < m_Nodes.Count - 1 && m_Nodes[i + 1].m_Type == "Array") //Array
                        {
                            if ((m_Nodes[i + 1].m_MetaFlag & 0x4000) != 0)
                                align = true;
                            var vector = GetNodes(m_Nodes, i);
                            i += vector.Count - 1;
                            var size = reader.ReadInt32();
                            if(size<0)throw new InvalidDataException("Negative array count at "+field);
                            var list = new List<object>();
                            for (int j = 0; j < size; j++)
                            {
                                int tmp = 3;
                                long before=reader.Position;
                                list.Add(ReadValue(vector, reader, ref tmp,field+"["+j+"]",reference,stringField,inRegistry,depth+1));
                                if(reader.Position==before&&size>100000)throw new InvalidDataException("Oversized zero-width array at "+field);
                            }
                            value = list;
                            break;
                        }
                        else //Class
                        {
                            var @class = GetNodes(m_Nodes, i);
                            i += @class.Count - 1;
                            var obj = new OrderedDictionary();
                            for (int j = 1; j < @class.Count; j++)
                            {
                                var classmember = @class[j];
                                var name = classmember.m_Name;
                                if(varTypeStr=="ReferencedObject"&&classmember.m_Type=="ReferencedObjectData")
                                {
                                    if(reader is not ObjectReader owner)throw new InvalidDataException("Managed reference requires its owning SerializedFile");
                                    var dynamicTree=ResolveManagedType(owner,obj);
                                    int root=0;
                                    obj[name]=dynamicTree==null?null:ReadValue(dynamicTree.m_Nodes,reader,ref root,field+"."+name,reference,stringField,inRegistry,depth+1);
                                    j+=GetNodes(@class,j).Count-1;
                                }
                                else obj[name] = ReadValue(@class, reader, ref j,field+"."+name,reference,stringField,inRegistry,depth+1);
                            }
                            value = obj;
                            break;
                        }
                    }
            }
            if(reference!=null && varTypeStr.StartsWith("PPtr<",StringComparison.Ordinal) && value is OrderedDictionary pointer && pointer.Contains("m_FileID") && pointer.Contains("m_PathID"))
                reference(field,Convert.ToInt32(pointer["m_FileID"]),Convert.ToInt64(pointer["m_PathID"]),fieldOffset);
            if (align)
                reader.AlignStream();
            return value;
        }

        private static TypeTree ResolveManagedType(ObjectReader reader,OrderedDictionary value)
        {
            if(value["type"] is not OrderedDictionary identity||identity["class"] is not string name||identity["ns"] is not string ns||identity["asm"] is not string assembly)
                throw new InvalidDataException("Incomplete managed reference type identity");
            if(name.Length==0)return null; // Null/missing managed object; no serialized data.
            var matches=reader.assetsFile.m_RefTypes?.Where(t=>t.m_KlassName==name&&t.m_NameSpace==ns&&t.m_AsmName==assembly).ToArray();
            if(matches==null||matches.Length!=1)throw new InvalidDataException("Missing or ambiguous managed reference type: "+assembly+" / "+ns+"."+name);
            return matches[0].m_Type??throw new InvalidDataException("Missing exact managed reference schema: "+assembly+" / "+ns+"."+name);
        }

        private static List<TypeTreeNode> GetNodes(List<TypeTreeNode> m_Nodes, int index)
        {
            var nodes = new List<TypeTreeNode>();
            nodes.Add(m_Nodes[index]);
            var level = m_Nodes[index].m_Level;
            for (int i = index + 1; i < m_Nodes.Count; i++)
            {
                var member = m_Nodes[i];
                var level2 = member.m_Level;
                if (level2 <= level)
                {
                    return nodes;
                }
                nodes.Add(member);
            }
            return nodes;
        }
    }
}
