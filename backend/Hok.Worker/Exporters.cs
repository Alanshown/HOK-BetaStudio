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
  Texture2D or Sprite=>["png","tga","bmp","jpg","raw"],
  Mesh=>["obj","json","raw"],
  Animator or GameObject when FbxReady=>["fbx","json","raw"],
  AudioClip when AudioReady&&AudioTools.Mp3Ready=>["wav","mp3","original","raw"],
  AudioClip when AudioReady=>["wav","original","raw"],
  AudioClip=>["original","raw"],
  AnimationClip=>["anim","json","raw"],
  TextAsset=>["original","json","raw"],
  Shader=>["shader","json","raw"],
  Font or VideoClip or MovieTexture=>["original","json","raw"],
  _=>["json","raw"]
 };
 public static string Extension(Obj obj,string format)=>format switch {
  "raw"=>"dat","original"=>obj switch{AudioClip a=>new AudioClipConverter(a).GetExtensionName().TrimStart('.'),Font f=>f.m_FontData?.Take(4).SequenceEqual("OTTO"u8.ToArray())==true?"otf":"ttf",VideoClip v=>SafeExt(Path.GetExtension(v.m_OriginalPath),"video"),MovieTexture=>"ogv",TextAsset t=>SafeExt(Path.GetExtension(t.Name),"bytes"),_=>"bin"},_=>format};
 static string SafeExt(string ext,string fallback){var s=ext.TrimStart('.');return s.Length is >0 and <12 && s.All(char.IsAsciiLetterOrDigit)?s:fallback;}
 public static string ToJson(Obj obj){object value=obj;if(obj is MonoBehaviour){value=obj.ToType()??(object)new{type=obj.type.ToString(),pathId=obj.m_PathID.ToString(),note="Type tree unavailable; raw bytes are available."};}
  return JsonConvert.SerializeObject(value,Formatting.Indented,new JsonSerializerSettings{ReferenceLoopHandling=ReferenceLoopHandling.Ignore,MaxDepth=64,Converters={new StringEnumConverter()}});
 }
 public static void Write(Obj obj,string format,string path,ExportOptions options) {
  if(!Formats(obj).Contains(format))throw new NotSupportedException(format);
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  switch(format){
   case "raw":File.WriteAllBytes(path,obj.GetRawData());break;
   case "json":File.WriteAllText(path,ToJson(obj));break;
   case "png":case "tga":case "jpg":case "bmp":
    using(var image=obj switch{Texture2D t=>t.ConvertToImage(true),Sprite s=>s.GetImage(),_=>null}){
     if(image is null)throw new InvalidDataException("Image decoding failed");using var stream=File.Create(path);image.WriteToStream(stream,format switch{"tga"=>ImageFormat.Tga,"bmp"=>ImageFormat.Bmp,"jpg"=>ImageFormat.Jpeg,_=>ImageFormat.Png});
    }break;
   case "obj":WriteObj((Mesh)obj,path);break;
   case "wav":var wav=new AudioClipConverter((AudioClip)obj).ConvertToWav();if(wav is null||wav.Length<=44)throw new InvalidDataException("Audio decoding failed");File.WriteAllBytes(path,wav);break;
   case "mp3":
    var temporaryWave=Path.Combine(AudioTools.Scratch,Guid.NewGuid().ToString("N")+".wav");
    try{Write(obj,"wav",temporaryWave,options);AudioTools.Mp3FromWave(temporaryWave,path);}finally{if(File.Exists(temporaryWave))File.Delete(temporaryWave);}
    break;
   case "shader":File.WriteAllText(path,((Shader)obj).Convert());break;
   case "anim":var yaml=((AnimationClip)obj).Convert();if(string.IsNullOrWhiteSpace(yaml))throw new InvalidDataException("Animation conversion unavailable");File.WriteAllText(path,yaml);break;
   case "fbx":WriteFbx(obj,path,options);break;
   case "original":
    var data=obj switch{TextAsset t=>t.m_Script,Font f=>f.m_FontData,AudioClip a=>a.m_AudioData.GetData(),VideoClip v=>v.m_VideoData.GetData(),MovieTexture m=>m.m_MovieData,_=>throw new NotSupportedException()};
    if(data is null||data.Length==0)throw new InvalidDataException("Source stream is empty");File.WriteAllBytes(path,data);break;
   default:throw new NotSupportedException(format);
  }
 }
 static void WriteFbx(Obj obj,string path,ExportOptions settings) {
  if(!float.IsFinite(settings.Scale)||settings.Scale<=0||settings.Scale>1000)throw new ArgumentOutOfRangeException(nameof(settings.Scale));
  var options=new ModelConverter.Options {game=obj.assetsFile.game,imageFormat=ImageFormat.Png,collectAnimations=settings.Animations,exportMaterials=settings.Materials,materials=[],uvs=Enumerable.Range(0,8).ToDictionary(i=>"UV"+i,i=>(settings.AllUv||i==0,i)),texs=[]};
  IImported model=obj switch{Animator a=>new ModelConverter(a,options),GameObject g=>new ModelConverter(g,options),_=>throw new NotSupportedException()};
  var dir=Directory.GetCurrentDirectory();try{ModelExporter.ExportFbx(path,model,new Fbx.ExportOptions{eulerFilter=true,filterPrecision=.25f,exportAllNodes=true,exportSkins=true,exportAnimations=settings.Animations,exportBlendShape=settings.BlendShapes,boneSize=10,scaleFactor=settings.Scale,fbxVersion=3,fbxFormat=0});}finally{Directory.SetCurrentDirectory(dir);}
  if(settings.Materials)foreach(var mat in options.materials)File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!,Identity.SafeName(mat.Name)+"_"+mat.m_PathID+".material.json"),ToJson(mat));
 }
 static void WriteObj(Mesh m,string path) {
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
