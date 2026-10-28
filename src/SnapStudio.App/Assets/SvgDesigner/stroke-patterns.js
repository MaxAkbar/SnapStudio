(() => {
 'use strict';
 const NS='http://www.w3.org/2000/svg',tags=new Set(['rect','circle','ellipse','path','line','polyline','polygon']);
 const attributes=['d','x','y','x1','y1','x2','y2','width','height','cx','cy','r','rx','ry','points'];
 window.createSvgStrokePatterns=({root})=>{
  let library=[],current=null;
  const $=id=>document.getElementById(id),make=(name,attrs={})=>{const el=document.createElementNS(NS,name);for(const[k,v]of Object.entries(attrs))el.setAttribute(k,v);return el;};
  const isDerived=el=>!!el.closest('[data-snap-stroke-derived],[data-snap-stroke-defs]');
  const eligible=elements=>[...new Set(elements.flatMap(el=>tags.has(el.localName)?[el]:[...el.querySelectorAll([...tags].join(','))]))].filter(el=>!isDerived(el)&&!el.closest('defs,clipPath,mask,pattern,symbol'));
  const encode=value=>btoa(Array.from(new TextEncoder().encode(JSON.stringify(value)),byte=>String.fromCharCode(byte)).join(''));
  function read(el){try{const value=el.getAttribute('data-snap-stroke');return value?JSON.parse(new TextDecoder().decode(Uint8Array.from(atob(value),c=>c.charCodeAt(0)))):null;}catch{return null;}}
  function setLibrary(values){library=(values||[]).filter(p=>p.svg&&p.name);$('strokePattern').replaceChildren(new Option(library.length?'Choose a pattern…':'No saved patterns',''),...library.map((p,i)=>new Option(p.name,String(i))));}
  function chosen(){const index=$('strokePattern').value,pattern=index!==''?library[Number(index)]:current;return pattern?{svg:pattern.svg,name:pattern.name,scale:Number($('strokePatternScale').value)/100||1,spacing:Number($('strokePatternSpacing').value)/100||1}:null;}
  function apply(elements,pattern){
   if(!pattern?.svg)throw new Error('Choose a saved SVG pattern. Create patterns using Line style in the main editor.');
   pattern={...pattern,scale:Math.max(.1,Math.min(10,Number(pattern.scale)||1)),spacing:Math.max(.1,Math.min(10,Number(pattern.spacing)||1))};
   const source=new DOMParser().parseFromString(pattern.svg,'image/svg+xml');if(source.querySelector('parsererror')||source.documentElement.localName!=='svg')throw new Error('The pattern SVG is invalid.');
   if(pattern.svg.length>64000||source.querySelectorAll('*').length>512)throw new Error('Use a repeating motif with at most 512 elements and 64 KB of SVG.');
   for(const el of eligible(elements)){
    const previous=read(el),originalOpacity=previous?.originalOpacity||{attribute:el.getAttribute('stroke-opacity'),style:el.style.getPropertyValue('stroke-opacity'),priority:el.style.getPropertyPriority('stroke-opacity')};
    el.setAttribute('data-snap-stroke',encode({...pattern,originalOpacity}));if(!el.hasAttribute('data-snap-stroke-id'))el.setAttribute('data-snap-stroke-id','p'+crypto.randomUUID().replaceAll('-',''));
    el.style.setProperty('stroke-opacity','0','important');
   }current=pattern;
  }
  function remove(elements){for(const el of eligible(elements)){const previous=read(el);if(!previous)continue;const opacity=previous.originalOpacity;
   if(opacity?.attribute===null)el.removeAttribute('stroke-opacity');else if(opacity?.attribute!==undefined)el.setAttribute('stroke-opacity',opacity.attribute);
   if(opacity?.style)el.style.setProperty('stroke-opacity',opacity.style,opacity.priority||'');else el.style.removeProperty('stroke-opacity');
   for(const key of ['data-snap-stroke','data-snap-stroke-id','data-snap-stroke-shape'])el.removeAttribute(key);
  }}
  function updateControls(elements){const pattern=eligible(elements).map(read).find(Boolean);$('customStrokeFields').hidden=$('dash').value!=='custom'&&!pattern;
   if(pattern){current=pattern;$('dash').value='custom';const index=library.findIndex(p=>p.svg===pattern.svg);$('strokePattern').value=index>=0?String(index):'';$('strokePatternScale').value=(pattern.scale||1)*100;$('strokePatternSpacing').value=(pattern.spacing||1)*100;}
  }
  const tiles=new Map();
  function tile(svg){
   if(tiles.has(svg))return tiles.get(svg).cloneNode(true);
   // Resolve motif CSS in a separate shadow root, then embed explicit paint styles.
   const host=document.createElement('div');host.style.cssText='position:fixed;left:-10000px;top:0;opacity:0;pointer-events:none';const shadow=host.attachShadow({mode:'open'});
   const element=document.importNode(new DOMParser().parseFromString(svg,'image/svg+xml').documentElement,true);shadow.append(element);document.body.append(host);
   for(const el of [element,...element.querySelectorAll('*')]){const style=getComputedStyle(el);
    for(const key of ['fill','fill-opacity','fill-rule','stroke','stroke-width','stroke-opacity','stroke-linecap','stroke-linejoin','stroke-dasharray','stroke-dashoffset','opacity','stop-color','stop-opacity','font-family','font-size','font-weight','font-style','text-anchor','display','visibility','clip-path','mask']){
     const value=style.getPropertyValue(key).replace(/url\(["']?[^)]*?#([^)'" ]+)["']?\)/g,'url(#$1)');if(value)el.style.setProperty(key,value,'important');
    }
   }element.querySelectorAll('style').forEach(el=>el.remove());host.remove();tiles.set(svg,element);return element.cloneNode(true);
  }
  function prefixIds(element,prefix){
   const ids=new Map();for(const el of [element,...element.querySelectorAll('[id]')])if(el.id){ids.set(el.id,prefix+el.id);el.id=prefix+el.id;}
   for(const el of [element,...element.querySelectorAll('*')])for(const a of [...el.attributes]){if(a.name.startsWith('data-'))continue;
    if(a.localName==='href'&&a.value[0]==='#'&&ids.has(a.value.slice(1)))a.value='#'+ids.get(a.value.slice(1));
    else a.value=a.value.replace(/url\(["']?#([^)'" ]+)["']?\)/g,(whole,id)=>ids.has(id)?`url(#${ids.get(id)})`:whole);
   }
  }
  function render(){
   const svg=root();svg.querySelectorAll('[data-snap-stroke-derived],[data-snap-stroke-defs]').forEach(el=>el.remove());
   const owners=eligible([...svg.querySelectorAll('[data-snap-stroke]')]);if(!owners.length)return;
   const seen=new Set();for(const owner of owners){let id=owner.getAttribute('data-snap-stroke-id');if(!id||seen.has(id)){id='p'+crypto.randomUUID().replaceAll('-','');owner.setAttribute('data-snap-stroke-id',id);}seen.add(id);}
   const definitions=make('defs',{'data-snap-stroke-defs':''}),prepared=new Map();let serial=0;const limit=Math.max(1,Math.floor(1024/owners.length));
   for(const owner of owners){const pattern=read(owner);if(!pattern?.svg)continue;let definition=prepared.get(pattern.svg);
    if(!definition){const source=tile(pattern.svg),view=(source.getAttribute('viewBox')||'0 0 32 16').trim().split(/[\s,]+/).map(Number),id='snap-pattern-'+owners[0].getAttribute('data-snap-stroke-id')+'-'+serial++;
     prefixIds(source,id+'-');const group=make('g',{id});for(const child of [...source.childNodes])group.append(child);definitions.append(group);definition={id,view};prepared.set(pattern.svg,definition);
    }
    const length=owner.getTotalLength();if(!Number.isFinite(length)||length<.000001)continue;
    const width=Math.max(.1,parseFloat(getComputedStyle(owner).strokeWidth)||1),height=width*4*(pattern.scale||1),size=height/definition.view[3],pitch=Math.max(.25,definition.view[2]*size*(pattern.spacing||1),length/Math.max(1,limit-1));
    const parent=owner.parentElement,matrix=parent.getScreenCTM().inverse().multiply(owner.getScreenCTM());
    const derived=make('g',{'data-snap-stroke-derived':owner.getAttribute('data-snap-stroke-id'),transform:`matrix(${[matrix.a,matrix.b,matrix.c,matrix.d,matrix.e,matrix.f].map(n=>Math.round(n*1e6)/1e6).join(' ')})`,style:`display:inline!important;visibility:visible!important;opacity:${getComputedStyle(owner).opacity}!important`});
    const closed=['rect','circle','ellipse','polygon'].includes(owner.localName)||owner.localName==='path'&&/[zZ]\s*$/.test(owner.getAttribute('d')||'');
    for(let d=0,count=0;d<=length+1e-6&&(!closed||d<length-1e-6)&&count<limit;d+=pitch,count++){
     const p=owner.getPointAtLength(d),before=owner.getPointAtLength(Math.max(0,d-.05)),after=owner.getPointAtLength(Math.min(length,d+.05)),angle=Math.atan2(after.y-before.y,after.x-before.x)*180/Math.PI;
     derived.append(make('use',{href:'#'+definition.id,transform:`translate(${p.x} ${p.y}) rotate(${angle}) scale(${size}) translate(${-definition.view[0]-definition.view[2]/2} ${-definition.view[1]-definition.view[3]/2})`,style:'display:inline!important;visibility:visible!important'}));
    }
    owner.setAttribute('data-snap-stroke-shape',JSON.stringify({attributes:Object.fromEntries(attributes.map(key=>[key,owner.getAttribute(key)])),width}));owner.after(derived);
   }svg.insertBefore(definitions,svg.querySelector('[data-editor-overlay]'));
  }
  function resolve(target){const derived=target?.closest?.('[data-snap-stroke-derived]');return derived?root().querySelector(`[data-snap-stroke-id="${CSS.escape(derived.getAttribute('data-snap-stroke-derived'))}"]`):target;}
  return{setLibrary,chosen,apply,remove,render,updateControls,isDerived,resolve,read,eligible};
 };
})();
