using System.Globalization;
using System.Text;
using AssetStudio;
using Hok.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Obj=AssetStudio.Object;
namespace Hok.Worker;
internal static class Exporters {
 static bool FbxReady=>File.Exists(Path.Combine(AppContext.BaseDirectory,"x64/AssetStudio.FBXNative.dll"));
 static bool AudioReady=>File.Exists(Path.Combine(AppContext.BaseDirectory,"x64/fmod.dll"));
 public static string[] Formats(Obj obj) => obj switch {
  DeferredObject d when d.type==ClassIDType.Mesh=>MeshFormats(),
  DeferredObject d when d.type==ClassIDType.AnimationClip=>AnimationFormats(),
  DeferredObject d when d.type==ClassIDType.Shader=>["shader","json","raw"],
  DeferredObject d when d.type is ClassIDType.Font or ClassIDType.VideoClip or ClassIDType.MovieTexture=>["original","json","raw"],
  DeferredObject d when d.type==ClassIDType.AudioClip=>AudioFormats(),
  Cubemap=>["zip-png","png","json","raw"],
  Texture2D or Sprite=>["png","tga","bmp","jpg","raw"],
  Mesh=>MeshFormats(),
  Animator or GameObject when FbxReady=>["fbx","json","raw"],
  AudioClip when AudioReady&&AudioTools.Mp3Ready=>["wav","mp3","original","raw"],
  AudioClip when AudioReady=>["wav","original","raw"],
  AudioClip=>["original","raw"],
  AnimationClip=>AnimationFormats(),
  TextAsset=>["original","json","raw"],
  Shader=>["shader","json","raw"],
  Font or VideoClip or MovieTexture=>["original","json","raw"],
  _=>["json","raw"]
 };
 static string[] MeshFormats()=>FbxReady?["obj","fbx","json","raw"]:["obj","json","raw"];
 static string[] AnimationFormats()=>FbxReady?["anim","fbx","curves-json","json","raw"]:["anim","curves-json","json","raw"];
 static string[] AudioFormats()=>AudioReady?(AudioTools.Mp3Ready?["wav","mp3","original","raw"]:["wav","original","raw"]):["original","raw"];
 public static string Extension(Obj obj,string format){if(format=="original")obj=DeferredObject.Resolve(obj);return format switch {
  "zip-png"=>"zip",
  "curves-json"=>"json",
  "raw"=>"dat","original"=>obj switch{AudioClip a=>new AudioClipConverter(a).GetExtensionName().TrimStart('.'),Font f=>f.m_FontData?.Take(4).SequenceEqual("OTTO"u8.ToArray())==true?"otf":"ttf",VideoClip v=>SafeExt(Path.GetExtension(v.m_OriginalPath),"video"),MovieTexture=>"ogv",TextAsset t=>SafeExt(Path.GetExtension(t.Name),"bytes"),_=>"bin"},_=>format};}
 static string SafeExt(string ext,string fallback){var s=ext.TrimStart('.');return s.Length is >0 and <12 && s.All(char.IsAsciiLetterOrDigit)?s:fallback;}
 public static string ToJson(Obj obj){obj=DeferredObject.Resolve(obj);object value=obj;if(obj.UseTypeTree){value=obj.ToType()??throw new InvalidDataException("Validated type tree unavailable");}
  else if(obj is MonoBehaviour mono){
   obj.assetsFile.ParseStatuses.TryGetValue(obj.m_PathID,out var state);
   var raw=obj.GetRawData();int consumed=checked((int)(state?.ConsumedBytes??0));
   value=new{type=obj.type.ToString(),classId=(int)obj.type,pathId=obj.m_PathID.ToString(),parseStatus=state?.Status??"typed-partial",complete=state?.RemainingBytes==0,
    baseFields=new{mono.m_Name,mono.m_Enabled,gameObject=new{mono.m_GameObject.m_FileID,pathId=mono.m_GameObject.m_PathID.ToString()},script=new{mono.m_Script.m_FileID,pathId=mono.m_Script.m_PathID.ToString()}},
    managedTypes=obj.assetsFile.m_RefTypes?.Select(t=>new{index=t.m_ScriptTypeIndex,className=t.m_KlassName,nameSpace=t.m_NameSpace,assembly=t.m_AsmName,typeHash=t.m_OldTypeHash==null?null:Convert.ToHexString(t.m_OldTypeHash)}),
    typeDependencies=obj.serializedType?.m_TypeDependencies,consumedBytes=consumed,unparsedBytes=raw.Length-consumed,
    scriptSchema=HokScriptSchemas.Inspect(mono),
    unparsedHex=Convert.ToHexString(raw.AsSpan(consumed)),note="Base-parser byte counts remain separate from optional fingerprint-validated script schema coverage. Unknown field meanings are not complete semantic interpretation."};
  }
  else if(obj is ShaderVariantCollection variants){
   obj.assetsFile.ParseStatuses.TryGetValue(obj.m_PathID,out var state);
   var raw=obj.GetRawData();int consumed=checked((int)(state?.ConsumedBytes??0));
   value=new{type="ShaderVariantCollection",variants.m_Name,variants.m_Shaders,
    parseStatus=state?.Status,complete=state?.RemainingBytes==0,consumedBytes=consumed,unparsedBytes=raw.Length-consumed,
    unparsedHex=Convert.ToHexString(raw.AsSpan(consumed)),
    note="Shader references, keywords and pass types are decoded. Any HOK extension bytes remain explicitly unparsed; this export is not a complete platform pipeline-state cache."};
  }
  else if(obj is HokResourceVolumeContext volume){
   obj.assetsFile.ParseStatuses.TryGetValue(obj.m_PathID,out var state);var raw=obj.GetRawData();int consumed=checked((int)(state?.ConsumedBytes??0));
   value=new{type="ResourceVolumeContext",volume.m_Name,volume.ReferencedObjects,volume.ClassTypeHashes,volume.EmptyExtensionTable,
    parseStatus=state?.Status,semanticComplete=false,consumedBytes=consumed,unparsedBytes=raw.Length-consumed,
    unparsedHex=Convert.ToHexString(raw.AsSpan(consumed)),note="Resource-volume object and class/hash tables; not a hero/skin usage manifest. Trailing platform fields remain unknown."};
  }
  return JsonConvert.SerializeObject(value,Formatting.Indented,new JsonSerializerSettings{ReferenceLoopHandling=ReferenceLoopHandling.Ignore,MaxDepth=64,Converters={new StringEnumConverter()}});
 }
 public static string[] Write(Obj obj,string format,string path,ExportOptions options) {
  if(!Formats(obj).Contains(format))throw new NotSupportedException(format);
  if(format!="raw")obj=DeferredObject.Resolve(obj);
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  switch(format){
   case "zip-png":CubemapImages.WriteArchive((Cubemap)obj,path);break;
   case "curves-json":File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(AnimationCurves.Read((AnimationClip)obj),Program.Json));break;
   case "raw":File.WriteAllBytes(path,obj.GetRawData());break;
   case "json":File.WriteAllText(path,ToJson(obj));break;
   case "png":case "tga":case "jpg":case "bmp":
    using(var image=obj switch{Cubemap cube=>CubemapImages.ContactSheet(cube),Texture2D t=>t.ConvertToImage(true),Sprite s=>s.GetImage(),_=>null}){
     if(image is null)throw new InvalidDataException("Image decoding failed");using var stream=File.Create(path);image.WriteToStream(stream,format switch{"tga"=>ImageFormat.Tga,"bmp"=>ImageFormat.Bmp,"jpg"=>ImageFormat.Jpeg,_=>ImageFormat.Png});
    }break;
   case "obj":WriteObj((Mesh)obj,path);break;
   case "wav":var wav=new AudioClipConverter((AudioClip)obj).ConvertToWav();if(wav is null||wav.Length<=44)throw new InvalidDataException("Audio decoding failed");AudioValidation.ValidateWave(wav,true);AudioTools.WriteOriginal(wav,path);break;
   case "mp3":
    var temporaryWave=Path.Combine(AudioTools.Scratch,Guid.NewGuid().ToString("N")+".wav");
    try{Write(obj,"wav",temporaryWave,options);AudioTools.Mp3FromWave(temporaryWave,path);}finally{if(File.Exists(temporaryWave))File.Delete(temporaryWave);}
    break;
   case "shader":File.WriteAllText(path,((Shader)obj).Convert());break;
   case "anim":var yaml=((AnimationClip)obj).Convert();if(string.IsNullOrWhiteSpace(yaml))throw new InvalidDataException("Animation conversion unavailable");File.WriteAllText(path,yaml);break;
   case "fbx":return ModelFbxExport.Write(obj,path,options);
   case "original":
    var data=obj switch{TextAsset t=>t.m_Script,Font f=>f.m_FontData,AudioClip a=>a.m_AudioData.GetData(),VideoClip v=>v.m_VideoData.GetData(),MovieTexture m=>m.m_MovieData,_=>throw new NotSupportedException()};
    if(data is null||data.Length==0)throw new InvalidDataException("Source stream is empty");File.WriteAllBytes(path,data);break;
   default:throw new NotSupportedException(format);
  }
  return [];
 }
 static void WriteObj(Mesh m,string path) {
  MeshExportValidation.Validate(m);
  int count=m.m_VertexCount;if(count<=0||m.m_Vertices is null||m.m_Vertices.Length<count*3)throw new InvalidDataException("Mesh vertices unavailable");
  int vstride=m.m_Vertices.Length/count;bool uv=m.m_UV0?.Length>=count*2,normal=m.m_Normals?.Length>=count*3;int us=uv?m.m_UV0.Length/count:0,ns=normal?m.m_Normals.Length/count:0;
  using var sw=new StreamWriter(path,false,new UTF8Encoding(false));sw.WriteLine("# HOK Studio OBJ; source coordinates converted to right-handed");
  for(int i=0;i<count;i++)sw.WriteLine(FormattableString.Invariant($"v {-Finite(m.m_Vertices[i*vstride])} {Finite(m.m_Vertices[i*vstride+1])} {Finite(m.m_Vertices[i*vstride+2])}"));
  if(uv)for(int i=0;i<count;i++)sw.WriteLine(FormattableString.Invariant($"vt {Finite(m.m_UV0[i*us])} {Finite(m.m_UV0[i*us+1])}"));
  if(normal)for(int i=0;i<count;i++)sw.WriteLine(FormattableString.Invariant($"vn {-Finite(m.m_Normals[i*ns])} {Finite(m.m_Normals[i*ns+1])} {Finite(m.m_Normals[i*ns+2])}"));
  string Ref(uint index){if(index>=count)throw new InvalidDataException("Mesh index out of range");var v=(index+1).ToString(CultureInfo.InvariantCulture);return uv&&normal?$"{v}/{v}/{v}":uv?$"{v}/{v}":normal?$"{v}//{v}":v;}
  for(int i=0;i+2<m.m_Indices.Count;i+=3)sw.WriteLine($"f {Ref(m.m_Indices[i+2])} {Ref(m.m_Indices[i+1])} {Ref(m.m_Indices[i])}");
 }
 static float Finite(float v)=>float.IsFinite(v)?v:throw new InvalidDataException("Non-finite mesh coordinates");
}
