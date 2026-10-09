"""Independent packed-bit decoder checked against original serialized endpoint metadata.

Input is produced by an explicitly reviewed raw-object harness, never by a replacement
game extractor. Graph SHA-256 identity and raw packed word spans are checked first.
"""
import collections
import hashlib
import json
import math
from pathlib import Path
import struct
import sys
import numpy as np


def decode(d, positive_when_set):
    words=d['m_HokCompressedSamples']; ranges=d['m_HokRanges']
    frames=d['m_FrameCount']; curves=d['m_CurveCount']
    layouts=[(r['kind']&255,r['kind']>>8,{1:4,2:3,3:1}[r['kind']&255]) for r in ranges]
    bits=sum(dim*16 if not width else 3+width*3 if kind==1 else dim*width for kind,width,dim in layouts)
    stride=(bits+15)//16
    if bits and bits%16==0 and len(words)==frames*(stride+1):
        assert all(words[(f+1)*(stride+1)-1]==0 for f in range(frames))
        stride+=1
    assert len(words)==frames*stride
    result=np.empty((frames,curves),dtype=np.float32); endpoints=[]
    for frame in range(frames):
        cursor=frame*stride*16; column=0
        def read(width):
            nonlocal cursor
            value=0
            for i in range(width):
                value|=((words[cursor//16]>>(cursor%16))&1)<<i
                cursor+=1
            return value
        for group,(r,(kind,width,dim)) in enumerate(zip(ranges,layouts)):
            def value():
                return np.float32(np.float32(r['minimum'])+np.float32(read(width or 16))*np.float32(r['step']))
            if kind==1 and width:
                stored=[value() for _ in range(3)]; flag=read(3); omitted=flag&3
                missing=np.float32(math.sqrt(max(0,1-sum(float(x)**2 for x in stored))))
                if bool(flag&4)!=positive_when_set: missing=-missing
                q=stored[:]; q.insert(omitted,missing); result[frame,column:column+4]=q
                if frame in (0,frames-1): endpoints.append((frame,group,column,omitted,bool(flag&4)))
            else:
                result[frame,column:column+dim]=[value() for _ in range(dim)]
            column+=dim
        assert column==curves
    return result,endpoints


def main():
    root=Path(sys.argv[1]); graph=json.loads(Path(sys.argv[2]).read_text(encoding='utf-8'))
    mode=sys.argv[3]; assert mode in ('baseline','fixed')
    original={(str(n['entry']),n['path_id']):n for n in graph['nodes'] if '300100014000' in n['sourcePath']}
    records=json.loads((root/'SELECTED_RAW_OBJECTS.json').read_text()); results=[]; coverage=collections.Counter()
    total=old_failures=new_failures=0; max_old=max_new=0.0
    for record in records:
        if record['type']!=74: continue
        raw=(root/record['raw_file']).read_bytes(); node=original[(str(record['entry']),record['pathId'])]
        assert hashlib.sha256(raw).hexdigest()==record['raw_sha256']==node['sha256'].lower()
        assert record['error'] is None and record['consumed']==record['bytes']
        typed=json.loads((root/record['typed_file']).read_text()); parts=[]
        owners=[]
        if typed.get('m_MuscleClip'):
            muscle=typed['m_MuscleClip']; owners.append(('muscle',muscle['m_Clip'],muscle['m_ValueArrayDelta']))
        if typed.get('m_HokLegacyAnimation'):
            owners.append(('hok-legacy',typed['m_HokLegacyAnimation']['clip'],[]))
        for owner,clip,delta in owners:
            dense=clip['m_DenseClip']; ranges=dense.get('m_HokRanges') or []
            packed=any((r['kind']&255)==1 and (r['kind']>>8)>0 for r in ranges)
            part={'owner':owner,'packedQuaternion':packed}
            if packed:
                old,ends=decode(dense,False); proposed,_=decode(dense,True)
                actual=np.array(dense['m_SampleArray']).reshape(dense['m_FrameCount'],dense['m_CurveCount'])
                expected=old if mode=='baseline' else proposed
                decode_error=float(np.max(np.abs(expected-actual))); assert decode_error<2e-6
                words=dense['m_HokCompressedSamples']; blob=struct.pack('<'+'H'*len(words),*words)
                offset=raw.find(blob); assert offset>=4 and raw.find(blob,offset+1)==-1
                assert struct.unpack_from('<I',raw,offset-4)[0]==len(words)
                samples=[]; skipped=clip['m_StreamedClip']['curveCount']
                if delta:
                    # Endpoints must be separately serialized raw floats, not values
                    # synthesized by the same decoder being tested.
                    endpoint_values=np.array([v[k] for v in delta for k in ('m_Start','m_Stop')],dtype=np.float32)
                    count_bytes=struct.pack('<I',len(delta)); offset_search=0; positions=[]
                    while (count_offset:=raw.find(count_bytes,offset_search))>=0:
                        offset_search=count_offset+1; start=count_offset+4
                        if start+endpoint_values.nbytes>len(raw): continue
                        values=np.frombuffer(raw,dtype='<f4',count=len(endpoint_values),offset=start)
                        # JSON may normalize -0 to 0; compare exact float32 values,
                        # not a round-tripped negative-zero bit pattern.
                        if np.array_equal(values,endpoint_values): positions.append(start)
                    assert len(positions)==1,(record['name'],'ambiguous/missing raw endpoint array',positions)
                    part['serializedEndpointBytesOffset']=positions[0]
                for frame,group,column,omitted,sign in ends:
                    if not delta: continue
                    which='m_Start' if frame==0 else 'm_Stop'
                    target=np.array([delta[skipped+column+i][which] for i in range(4)])
                    before=old[frame,column:column+4]; after=proposed[frame,column:column+4]
                    tolerance=max(2e-6,float(ranges[group]['step'])*4)
                    eold=float(np.max(np.abs(before-target))); enew=float(np.max(np.abs(after-target)))
                    # Check orientation too: q and -q are equivalent, flipping one component is not.
                    orientation_error=float(min(np.linalg.norm(before-target),np.linalg.norm(before+target)))
                    samples.append({'frame':frame,'column':column,'omitted':omitted,'positiveBit':sign,'oldError':eold,'fixedError':enew,'tolerance':tolerance,'oldOrientationError':orientation_error})
                    coverage[(omitted,sign)]+=1;total+=1;old_failures+=int(eold>=tolerance);new_failures+=int(enew>=tolerance)
                    max_old=max(max_old,eold);max_new=max(max_new,enew)
                part.update(pythonVsCSharpMaxError=decode_error,packedWordsOffset=offset,endpoints=samples)
            parts.append(part)
        results.append({'name':record['name'],'pathId':record['pathId'],'rawSha256':record['raw_sha256'],'parts':parts})
    assert len(results)==62
    assert new_failures==0, 'Proposed decoder fails independent serialized endpoints'
    assert old_failures>0, 'The alleged bug was not reproduced'
    result={'mode':mode,'clips':len(results),'affectedClips':sum(any(p['packedQuaternion'] for p in r['parts']) for r in results),'rawIdentityVerified':True,'completeObjectConsumption':True,'endpointTests':total,'oldFailedEndpoints':old_failures,'fixedFailedEndpoints':new_failures,'oldMaxError':max_old,'fixedMaxError':max_new,'coverage':[{'omitted':k[0],'positiveBit':k[1],'count':v} for k,v in sorted(coverage.items())],'records':results,'scope':'Source identity, packed decoder and serialized endpoint validation; not full runtime animation/effect correctness.'}
    (root/'INDEPENDENT_QUATERNION_REVIEW.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='records'},ensure_ascii=False))


if __name__=='__main__': main()
