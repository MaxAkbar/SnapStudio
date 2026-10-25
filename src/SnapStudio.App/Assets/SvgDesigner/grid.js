(() => {
 'use strict';
 const NS='http://www.w3.org/2000/svg',MIN_DISPLAY_SPACING=8,TOLERANCE=6,P=window.snapPerspective;
 window.createSvgGrid=({root,frame,onChange})=>{
  let settings={visible:false,spacing:32,snap:true,...P.normalize()},lastKey='',lastScale={x:1,y:1},geometryKey='',geometry=[];
  const $=id=>document.getElementById(id),make=(tag,attrs)=>{const el=document.createElementNS(NS,tag);for(const [key,value]of Object.entries(attrs))el.setAttribute(key,String(value));return el;};
  // Artwork styles stay inside the frame's shadow root; guides have their own isolated surface.
  const host=document.createElement('div');host.setAttribute('data-editor-grid-host','');host.setAttribute('aria-hidden','true');
  host.style.cssText='position:absolute;pointer-events:none;z-index:1';frame.parentElement.append(host);
  const gridRoot=host.attachShadow({mode:'open'}),style=document.createElement('style');style.textContent='svg{display:block;width:100%;height:100%;overflow:hidden;pointer-events:none}svg[hidden]{display:none}';gridRoot.append(style);
  const overlay=make('svg',{'data-editor-grid':'','aria-hidden':'true',focusable:'false',preserveAspectRatio:'none'});gridRoot.append(overlay);
  const names=['Rectangular','1-point perspective','2-point perspective','3-point perspective','4-point curved','5-point fisheye'];
  for(let mode=0;mode<names.length;mode++){const button=document.createElement('button');button.type='button';button.dataset.gridMode=mode;button.setAttribute('aria-label',names[mode]);button.title=names[mode];const icon=make('svg',{viewBox:'0 0 24 24','aria-hidden':'true'});icon.append(make('path',{d:P.icons[mode]}));const label=document.createElement('span');label.textContent=mode?`${mode} pt`:'Grid';button.append(icon,label);button.onclick=()=>setSettings({mode,visible:true});$('gridModes').append(button);}
  function strokes(state){const key=JSON.stringify([state.view,P.normalize(settings)]);if(key!==geometryKey){geometryKey=key;geometry=P.create(state.view,settings);}return geometry;}
  const pathData=points=>points.map((p,i)=>`${i?'L':'M'}${p.x} ${p.y}`).join(' ');
  function showMatch(match){overlay.querySelector('[data-perspective-match]')?.remove();if(match)overlay.append(make('path',{d:pathData(match.stroke.points),fill:'none',stroke:'#d53fb5','stroke-width':1.5,'stroke-dasharray':'4 3','vector-effect':'non-scaling-stroke','data-perspective-match':''}));}
  function layout(){
   const svg=root(),v=svg.viewBox.baseVal,m=svg.getScreenCTM();
   if(m){const x=Math.hypot(m.a,m.b),y=Math.hypot(m.c,m.d);if(x>0&&y>0)lastScale={x,y};}
   let stride=1;while(settings.spacing*Math.min(lastScale.x,lastScale.y)*stride<MIN_DISPLAY_SPACING&&stride<1048576)stride*=2;
   return{...settings,effectiveSpacing:settings.spacing*stride,stride,scale:lastScale,view:{x:v.x,y:v.y,width:v.width,height:v.height}};
  }
  function render(){
   const state=layout(),v=state.view,key=JSON.stringify([settings,v,state.scale]);
   const rect=root().getBoundingClientRect(),stage=frame.parentElement,stageRect=stage.getBoundingClientRect();
   if(rect.width>0&&rect.height>0)Object.assign(host.style,{left:`${rect.left-stageRect.left+stage.scrollLeft-stage.clientLeft}px`,top:`${rect.top-stageRect.top+stage.scrollTop-stage.clientTop}px`,width:`${rect.width}px`,height:`${rect.height}px`});
   $('gridToggle').setAttribute('aria-pressed',String(settings.visible));$('gridSnap').checked=settings.snap;$('gridSnap').disabled=!settings.visible;
   $('gridModeIcon').replaceChildren(make('path',{d:P.icons[settings.mode]}));
   for(const button of $('gridModes').children)button.setAttribute('aria-pressed',String(Number(button.dataset.gridMode)===settings.mode));
   $('rectangularGridFields').hidden=!!settings.mode;$('perspectiveFields').hidden=!settings.mode;
   $('horizontalVanishingPoints').hidden=settings.mode<2||settings.mode>4;$('verticalVanishingPoint').hidden=settings.mode!==3;$('perspectiveRadiusField').hidden=settings.mode<4;
   $('perspectiveCenterField').hidden=settings.mode===2;
   for(const key of ['density','centerX','horizon','left','right','vertical','radius']){const input=$('grid'+key[0].toUpperCase()+key.slice(1));if(document.activeElement!==input)input.value=settings[key];}
   if(document.activeElement!==$('gridSpacing'))$('gridSpacing').value=settings.spacing;
   $('gridSpacingDescription').textContent=settings.mode?'Perspective positions are percentages of the SVG viewBox. Vanishing points can be outside the artwork.':state.stride===1?'Spacing is measured in SVG coordinate units.':`At this scale, visible lines are ${state.effectiveSpacing} SVG units apart. Enlarge the editor for a finer grid.`;
   overlay.toggleAttribute('hidden',!settings.visible);
   if(key===lastKey)return;lastKey=key;overlay.replaceChildren();overlay.setAttribute('viewBox',`${v.x} ${v.y} ${v.width} ${v.height}`);
   if(!settings.visible)return;
   if(settings.mode){for(const [index,stroke]of strokes(state).entries()){const attrs={d:pathData(stroke.points),fill:'none','vector-effect':'non-scaling-stroke'};overlay.append(make('path',{...attrs,stroke:stroke.major?'#ffffff78':'#ffffff4b','stroke-width':1.5}),make('path',{...attrs,stroke:stroke.major?'#1e415f91':'#1e415f5a','stroke-width':.75,'data-perspective-guide':index}));}return;}
   for(const axis of ['x','y']){
    const extent=axis==='x'?v.width:v.height,count=Math.floor(extent/state.effectiveSpacing);
    for(let i=0;i<=Math.min(count,100000);i++){
     const position=(axis==='x'?v.x:v.y)+i*state.effectiveSpacing,major=i%4===0;
     const attrs=axis==='x'?{x1:position,x2:position,y1:v.y,y2:v.y+v.height}:{y1:position,y2:position,x1:v.x,x2:v.x+v.width};
     overlay.append(make('line',{...attrs,stroke:major?'#ffffff78':'#ffffff4b','stroke-width':1.5,'vector-effect':'non-scaling-stroke'}),
      make('line',{...attrs,stroke:major?'#1e415f91':'#1e415f5a','stroke-width':.75,'vector-effect':'non-scaling-stroke','data-grid-axis':axis,'data-major':major}));
    }
   }
  }
  function setSettings(value={},notify=true){
   if(typeof value.visible==='boolean')settings.visible=value.visible;
   if(typeof value.snap==='boolean')settings.snap=value.snap;
   if(Object.hasOwn(value,'spacing')){const n=Number(value.spacing);settings.spacing=Number.isFinite(n)?Math.max(4,Math.min(512,Math.round(n))):32;}
   Object.assign(settings,P.normalize({...settings,...value}));
   render();if(notify)onChange({...settings});return getState();
  }
  function nearest(value,axis,state){
   const start=state.view[axis],extent=state.view[axis==='x'?'width':'height'],step=state.effectiveSpacing,index=Math.round((value-start)/step),position=start+index*step;
   return position>=start&&position<=start+extent&&Math.abs(position-value)*state.scale[axis]<=TOLERANCE+1e-6?position-value:null;
  }
  const active=(state,event)=>state.visible&&state.snap&&!event?.altKey;
  function snapPoint(p,event){
   const state=layout();if(!active(state,event)){showMatch(null);return p;}
   if(settings.mode){const match=P.find(p,strokes(state),TOLERANCE,null,null,state.scale.x,state.scale.y);showMatch(match);return match?.point||p;}
   return{x:p.x+(nearest(p.x,'x',state)??0),y:p.y+(nearest(p.y,'y',state)??0)};
  }
  function snapStraightLine(start,end,event,direction=null){
   const state=layout();if(!settings.mode||!active(state,event)){showMatch(null);return null;}
   const result=P.constrainLine(start,end,state.view,settings,state.scale.x,state.scale.y,direction);
   showMatch(result.stroke?{stroke:result.stroke}:null);return result;
  }
  function snapPerspectiveLine(start,end,event,direction=null){
   const state=layout();if(!settings.mode||!active(state,event)){showMatch(null);return{points:[start,end],stroke:null,direction:null};}
   const result=P.curveLine(start,end,state.view,settings,state.scale.x,state.scale.y,direction);
   showMatch(result.stroke?{stroke:result.stroke}:null);return result;
  }
  function snapRectangle(start,end,event,plane){
   const state=layout(),enabled=!!settings.mode&&active(state,event);
   const result=enabled?P.rectangle(start,end,state.view,settings,plane):P.flatRectangle(start,end);
   showMatch(enabled&&result?{stroke:{points:result.outline}}:null);return result;
  }
  // Shift constrains a direction or aspect ratio. Snap along that ray, never independently on X/Y.
  function alongRay(anchors,direction,state){
   let best=null;const length=Math.hypot(direction.x,direction.y);if(length<1e-9)return{x:0,y:0};
   const unit={x:direction.x/length,y:direction.y/length};
   for(const anchor of anchors)for(const axis of ['x','y']){
    if(Math.abs(unit[axis])<1e-9)continue;const delta=nearest(anchor[axis],axis,state);if(delta===null)continue;
    const distance=delta/unit[axis],change={x:unit.x*distance,y:unit.y*distance},pixels=Math.hypot(change.x*state.scale.x,change.y*state.scale.y);
    if(pixels<=TOLERANCE+1e-6&&(!best||pixels<best.pixels))best={...change,pixels};
   }
   return best||{x:0,y:0};
  }
  function snapRay(start,end,event){const state=layout();if(!active(state,event)){showMatch(null);return end;}if(settings.mode){const match=P.find(end,strokes(state),TOLERANCE,{x:end.x-start.x,y:end.y-start.y},null,state.scale.x,state.scale.y);showMatch(match);return match?.point||end;}const delta=alongRay([end],{x:end.x-start.x,y:end.y-start.y},state);return{x:end.x+delta.x,y:end.y+delta.y};}
  function snapMove(box,delta,event){
   const state=layout();if(!active(state,event)){showMatch(null);return delta;}
   if(settings.mode){let best=null,change={x:0,y:0};for(const u of [0,.5,1])for(const v of [0,.5,1]){const anchor={x:box.x+box.width*u+delta.x,y:box.y+box.height*v+delta.y},match=P.find(anchor,strokes(state),TOLERANCE,event?.shiftKey?delta:null,null,state.scale.x,state.scale.y);if(match&&(!best||match.distance<best.distance-1e-8)){best=match;change={x:match.point.x-anchor.x,y:match.point.y-anchor.y};}}showMatch(best);return{x:delta.x+change.x,y:delta.y+change.y};}
   const anchors=[0,.5,1].map(n=>({x:box.x+box.width*n+delta.x,y:box.y+box.height*n+delta.y}));
   if(event?.shiftKey){const change=alongRay(anchors,delta,state);return{x:delta.x+change.x,y:delta.y+change.y};}
   const correction={x:0,y:0};for(const axis of ['x','y']){let best=null;for(const anchor of anchors){const candidate=nearest(anchor[axis],axis,state);if(candidate!==null&&(best===null||Math.abs(candidate)<Math.abs(best)))best=candidate;}correction[axis]=best??0;}
   return{x:delta.x+correction.x,y:delta.y+correction.y};
  }
  const getState=()=>{const{effectiveSpacing,stride}=layout();return{...settings,effectiveSpacing,stride};};
  for(const key of ['density','centerX','horizon','left','right','vertical','radius']){const input=$('grid'+key[0].toUpperCase()+key.slice(1));input.onchange=()=>{setSettings({[key]:Number(input.value)});input.value=settings[key];};}
  $('resetPerspective').onclick=()=>setSettings({...P.normalize(),mode:settings.mode});
  document.addEventListener('pointerup',()=>showMatch(null));document.addEventListener('pointercancel',()=>showMatch(null));
  $('gridToggle').onclick=()=>setSettings({visible:!settings.visible});
  $('gridSpacing').onchange=()=>{setSettings({spacing:$('gridSpacing').value});$('gridSpacing').value=settings.spacing;};
  $('gridSnap').onchange=()=>setSettings({snap:$('gridSnap').checked});
  const panel=$('gridPanel'),toggle=$('gridSettingsToggle');
  function close(){panel.hidden=true;toggle.setAttribute('aria-expanded','false');}
  toggle.onclick=()=>{panel.hidden=!panel.hidden;toggle.setAttribute('aria-expanded',String(!panel.hidden));if(!panel.hidden)$('gridSpacing').focus();};
  document.addEventListener('pointerdown',e=>{if(!panel.hidden&&!panel.contains(e.target)&&!toggle.contains(e.target))close();});
  document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!panel.hidden){close();toggle.focus();e.preventDefault();e.stopImmediatePropagation();}},true);
  new ResizeObserver(render).observe(frame);
  return{render,setSettings,snapPoint,snapRay,snapMove,snapStraightLine,snapPerspectiveLine,snapRectangle,clearMatch:()=>showMatch(null),getState};
 };
})();
