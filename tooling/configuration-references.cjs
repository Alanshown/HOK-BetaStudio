// XML field/condition evidence already validated by HokXmlData. An audio
// event name is a symbolic call, not a filesystem path or confirmed bank ID.
function xmlReferences(document){
 const tracks=new Map((document.tracks??[]).map(t=>[t.guid,t]));
 return (document.references??[]).flatMap(ref=>{
  const audio=ref.field==='eventName'&&ref.eventName==='PlayHeroSoundTick'&&ref.path.length>0;
  const resource=ref.path.includes('/')&&!/^https?:/i.test(ref.path);
  if(!audio&&!resource)return [];
  const track=tracks.get(ref.trackGuid);
  return [{...ref,referenceKind:audio?'audio-event-name':'resource-path',
   runtimeApplicability:'conditional-not-evaluated',
   trackContext:track?{guid:track.guid,name:track.name,eventType:track.eventType,enabled:track.enabled,
    conditions:track.conditions??[],skinFilters:track.skinFilters??[],skinOrAvatarIds:track.skinOrAvatarIds??[]}:null}];
 });
}
module.exports={xmlReferences};
