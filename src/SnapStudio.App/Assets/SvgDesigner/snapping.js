(() => {
 'use strict';
 const NS='http://www.w3.org/2000/svg',TOLERANCE=6,EPSILON=1e-6;
 window.createSvgObjectSnapping=({root,frame,onChange})=>{
  let enabled=true,guides=[],moving=null;
  const toggle=document.getElementById('objectSnapToggle'),stage=frame.parentElement;
  // Keep alignment guides outside artwork so imported styles and SVG exports cannot affect them.
  const host=document.createElement('div');host.setAttribute('data-editor-snap-host','');host.setAttribute('aria-hidden','true');
  host.style.cssText='position:absolute;pointer-events:none;z-index:2';stage.append(host);
  const surface=host.attachShadow({mode:'open'}),style=document.createElement('style');
  style.textContent='svg{display:block;width:100%;height:100%;overflow:visible;pointer-events:none}';surface.append(style);
  const overlay=document.createElementNS(NS,'svg');overlay.setAttribute('data-editor-snap-guides','');overlay.setAttribute('preserveAspectRatio','none');surface.append(overlay);
  const finite=b=>b&&['x','y','width','height'].every(key=>Number.isFinite(b[key]));
  const anchor=(b,axis,index)=>b[axis]+b[axis==='x'?'width':'height']*index/2;
  function context(){
   const svg=root(),v=svg.viewBox.baseVal,m=svg.getScreenCTM();
   return{view:{x:v.x,y:v.y,width:v.width,height:v.height},scale:{x:Math.hypot(m?.a||0,m?.b||0),y:Math.hypot(m?.c||0,m?.d||0)}};
  }
  function better(a,b){
   if(!b)return true;
   if(Math.abs(a.distance-b.distance)>EPSILON)return a.distance<b.distance;
   if(a.same!==b.same)return a.same;
   if(a.canvas!==b.canvas)return !a.canvas;
   return a.position<b.position;
  }
  function snap(box,targets,event,point=false,direction=null){
   const state=context(),matches={x:null,y:null},original={x:box.x,y:box.y};
   if(!enabled||event?.altKey||!finite(box)||!state.scale.x||!state.scale.y)return{point:original,guides:[]};
   const v=state.view,list=targets.filter(box=>finite(box)&&box.x+box.width>=v.x&&box.x<=v.x+v.width&&box.y+box.height>=v.y&&box.y<=v.y+v.height).map(box=>({box,canvas:false}));list.push({box:state.view,canvas:true});
   const length=direction?Math.hypot(direction.x,direction.y):0,unit=length>EPSILON?{x:direction.x/length,y:direction.y/length}:null;
   let ray=null;
   for(const axis of ['x','y'])for(const target of list)for(const from of point?[0]:[0,1,2])for(const to of [0,1,2]){
    const position=anchor(target.box,axis,to),delta=position-anchor(box,axis,from);
    if(position<state.view[axis]-EPSILON||position>state.view[axis]+state.view[axis==='x'?'width':'height']+EPSILON)continue;
    const candidate={axis,position,delta,target:target.box,canvas:target.canvas,same:!point&&from===to,from,to,distance:Math.abs(delta)*state.scale[axis]};
    if(direction){
     if(!unit||Math.abs(unit[axis])<EPSILON)continue;
     const distance=delta/unit[axis],change={x:unit.x*distance,y:unit.y*distance};candidate.distance=Math.hypot(change.x*state.scale.x,change.y*state.scale.y);candidate.change=change;
     if(candidate.distance<=TOLERANCE+EPSILON&&better(candidate,ray))ray=candidate;
    }else if(candidate.distance<=TOLERANCE+EPSILON&&better(candidate,matches[axis]))matches[axis]=candidate;
   }
   let result;
   if(direction){
    result={x:box.x+(ray?.change.x||0),y:box.y+(ray?.change.y||0)};
    if(ray)matches[ray.axis]=ray;
   }else result={x:box.x+(matches.x?.delta||0),y:box.y+(matches.y?.delta||0)};
   return{point:result,guides:Object.values(matches).filter(Boolean)};
  }
  // Grid snapping computes its own candidate. Compare from the unsnapped pointer, so guides
  // describe the chosen alignment rather than a second correction to an already snapped point.
  function choose(raw,object,grid,gridMatch,event){
   const state=context(),coupled=event?.shiftKey||gridMatch.coupled;
   let result={x:grid.x,y:grid.y},chosen=[];
   if(coupled){
    const distance=p=>Math.hypot((p.x-raw.x)*state.scale.x,(p.y-raw.y)*state.scale.y);
    if(object.guides.length&&(!(gridMatch.x||gridMatch.y)||distance(object.point)<=distance(grid)+EPSILON)){result=object.point;chosen=object.guides;}
   }else for(const axis of ['x','y']){
    const match=object.guides.find(guide=>guide.axis===axis);
    if(match&&(!gridMatch[axis]||Math.abs(object.point[axis]-raw[axis])<=Math.abs(grid[axis]-raw[axis])+EPSILON)){result[axis]=object.point[axis];chosen.push(match);}
   }
   guides=chosen;return result;
  }
  function clear(){guides=[];moving=null;overlay.replaceChildren();host.hidden=true;}
  function render(box=moving){
   moving=box;overlay.replaceChildren();host.hidden=!guides.length||!finite(box);if(host.hidden)return;
   const svg=root(),v=context().view,rect=svg.getBoundingClientRect(),stageRect=stage.getBoundingClientRect();
   if(rect.width<=0||rect.height<=0){host.hidden=true;return;}
   Object.assign(host.style,{left:`${rect.left-stageRect.left+stage.scrollLeft-stage.clientLeft}px`,top:`${rect.top-stageRect.top+stage.scrollTop-stage.clientTop}px`,width:`${rect.width}px`,height:`${rect.height}px`});
   overlay.setAttribute('viewBox',`${v.x} ${v.y} ${v.width} ${v.height}`);
   for(const guide of guides){
    if(![0,1,2].some(index=>Math.abs(anchor(box,guide.axis,index)-guide.position)<.01))continue;
    const vertical=guide.axis==='x',axis=vertical?'y':'x',size=vertical?'height':'width';
    const from=guide.canvas?v[axis]:Math.max(v[axis],Math.min(box[axis],guide.target[axis]));
    const to=guide.canvas?v[axis]+v[size]:Math.min(v[axis]+v[size],Math.max(box[axis]+box[size],guide.target[axis]+guide.target[size]));
    if(to<from)continue;
    const line=document.createElementNS(NS,'line');
    for(const [name,value]of Object.entries({x1:vertical?guide.position:from,y1:vertical?from:guide.position,x2:vertical?guide.position:to,y2:vertical?to:guide.position,stroke:'#d53fb5','stroke-width':1,'vector-effect':'non-scaling-stroke','data-snap-axis':guide.axis}))line.setAttribute(name,value);
    overlay.append(line);
   }
  }
  function setEnabled(value){enabled=!!value;toggle.setAttribute('aria-pressed',String(enabled));clear();onChange?.();}
  toggle.onclick=()=>setEnabled(!enabled);setEnabled(true);
  return{snap,choose,render,clear,setEnabled,get enabled(){return enabled;},get hasGuides(){return guides.length>0;}};
 };
})();
