using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace AssetStudio;

public sealed record ReferenceObservation(string Field,int FileId,long PathId,long? SerializedOffset,long? ObjectOffset,
    string Origin,string ExpectedType,ReferenceResolution Resolution);

public static class ReferenceCollector
{
    public static List<ReferenceObservation> Collect(Object owner,List<string> issues,Action<string,string,long> stringField=null)
    {
        var result=new List<ReferenceObservation>();
        var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object value,string path,int depth)
        {
            if(value==null)return;
            var type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||value is string or decimal)return;
            if(depth>64){issues.Add("Unparsed reference structure beyond depth 64: "+path);return;}
            if(value is IObjectReference pointer)
            {
                result.Add(new(path,pointer.FileId,pointer.PathId,pointer.SerializedByteOffset,pointer.ObjectByteOffset,"typed-field",pointer.ExpectedType,pointer.ResolveEvidence()));
                return;
            }
            if(!type.IsValueType&&!seen.Add(value))return;
            if(type.IsArray && (type.GetElementType().IsPrimitive||type.GetElementType().IsEnum))return;
            if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(KeyValuePair<,>))
            {
                Walk(type.GetProperty("Key").GetValue(value),path+".key",depth+1);
                Walk(type.GetProperty("Value").GetValue(value),path+".value",depth+1);return;
            }
            if(value is IEnumerable sequence)
            {
                if(type.IsGenericType && type.GetGenericArguments().All(t=>t.IsPrimitive||t.IsEnum||t==typeof(string)))return;
                int i=0;foreach(var item in sequence)Walk(item,path+"["+(i++)+"]",depth+1);return;
            }
            if(type.Namespace==null||!type.Namespace.StartsWith("AssetStudio",StringComparison.Ordinal))return;
            foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))
            {
                if(field.Name is "assetsFile" or "reader" or "serializedType" or "version" or "assetsManager")continue;
                var next=field.GetValue(value);if(next is Object && !ReferenceEquals(next,owner))continue;
                Walk(next,path.Length==0?field.Name:path+"."+field.Name,depth+1);
            }
        }
        Walk(owner,"",0);
        if(owner.UseTypeTree&&owner.serializedType?.m_Type!=null)
        {
            long saved=owner.reader.Position;
            try
            {
                var typed=new List<ReferenceObservation>();
                var strings=stringField==null?null:new List<(string Field,string Value,long Offset)>();
                TypeTreeHelper.ReadType(owner.serializedType.m_Type,owner.reader,(field,fileId,pathId,offset)=>
                    typed.Add(new(field,fileId,pathId,offset,offset-owner.reader.byteStart,"validated-type-tree","",
                        owner.assetsFile.assetsManager.ResolveReference(owner.assetsFile,fileId,pathId))),strings==null?null:(field,value,offset)=>strings.Add((field,value,offset)));
                if(owner.reader.Position-owner.reader.byteStart!=owner.byteSize)throw new System.IO.InvalidDataException("Type tree did not consume the complete object");
                result.AddRange(typed.Where(r=>!result.Any(t=>t.SerializedOffset==r.SerializedOffset&&t.FileId==r.FileId&&t.PathId==r.PathId)));
                if(strings!=null)foreach(var text in strings)stringField(text.Field,text.Value,text.Offset);
            }
            catch(Exception e){issues.Add("Type tree reference collection: "+e.GetBaseException().Message);}
            finally{owner.assetsFile.reader.Position=saved;}
        }
        return result;
    }
}
