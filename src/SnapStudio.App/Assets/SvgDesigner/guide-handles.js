(() => {
 'use strict';
 const NS='http://www.w3.org/2000/svg',P=window.snapPerspective;
 window.createPerspectiveGuideHandles=({frame,root,getState,setSettings,isEnabled,onSelect})=>{
  const host=document.createElement('div');host.dataset.editorGuideHandlesHost='';host.style.cssText='position:absolute;pointer-events:none;z-index:4';frame.parentElement.append(host);
  const shadow=host.attachShadow({mode:'open'}),style=document.createElement('style');
  style.textContent='svg{display:block;width:100%;height:100%;overflow:hidden;pointer-events:none} [data-guide-handle]{pointer-events:all;cursor:grab;outline:none} [data-guide-handle=horizon]{cursor:ns-resize} [data-guide-handle]:focus .point{stroke-width:3} text{font:11px Segoe UI,sans-serif;user-select:none;pointer-events:none}';shadow.append(style);
  const make=(tag,attrs)=>{const el=document.createElementNS(NS,tag);for(const [key,value]of Object.entries(attrs))el.setAttribute(key,String(value));return el;};
  const svg=make('svg',{'data-editor-guide-handles':'',preserveAspectRatio:'none',role:'group','aria-label':'Perspective guide points'});shadow.append(svg);
  let drag=null;
  const point=e=>new DOMPoint(e.clientX,e.clientY).matrixTransform(svg.getScreenCTM().inverse());
  function geometry(settings){const {families,...value}=P.normalize(settings);return value;}
  function cancel(restore=true){const old=drag;drag=null;if(old&&svg.hasPointerCapture(old.id))svg.releasePointerCapture(old.id);if(old&&restore)setSettings(geometry(old.settings));}
  function render(state){
   const rect=root().getBoundingClientRect(),stage=frame.parentElement,stageRect=stage.getBoundingClientRect();
   const visible=isEnabled()&&state.visible&&state.mode&&rect.width>0&&rect.height>0;
   host.hidden=!visible;if(!visible){cancel(false);return;}
   if(drag&&(state.mode!==drag.settings.mode||['x','y','width','height'].some(k=>state.view[k]!==drag.view[k])))cancel(false);
   Object.assign(host.style,{left:`${rect.left-stageRect.left+stage.scrollLeft-stage.clientLeft}px`,top:`${rect.top-stageRect.top+stage.scrollTop-stage.clientTop}px`,width:`${rect.width}px`,height:`${rect.height}px`});
   const focus=shadow.activeElement?.dataset.guideHandle,v=state.view,sx=state.scale.x,sy=state.scale.y;
   svg.setAttribute('viewBox',`${v.x} ${v.y} ${v.width} ${v.height}`);svg.replaceChildren();
   const cy=Math.max(v.y+8/sy,Math.min(v.y+v.height-8/sy,v.y+v.height*state.horizon/100));
   const horizon=make('g',{'data-guide-handle':'horizon',role:'button',tabindex:0,'aria-label':`Move horizon, ${state.horizon.toFixed(1)} percent`});
   horizon.append(make('path',{d:`M${v.x} ${cy}H${v.x+v.width}`,stroke:'transparent','stroke-width':12/sy,fill:'none'}),make('path',{d:`M${v.x} ${cy}H${v.x+v.width}`,stroke:'#64748b','stroke-width':1/sy,'stroke-dasharray':`${5/sx} ${5/sx}`,fill:'none'}));
   const label=make('text',{x:v.x+8/sx,y:Math.max(v.y+14/sy,cy-9/sy),fill:'#64748b',style:`font-size:${11/sy}px`});label.textContent=`Horizon ${Math.round(state.horizon)}%`;horizon.append(label);svg.append(horizon);
   for(const handle of P.guideHandles(v,state)){
    const ink=state.families[handle.family];if(ink.opacity===0)continue;
    const x=Math.max(v.x+12/sx,Math.min(v.x+v.width-12/sx,handle.point.x)),y=Math.max(v.y+15/sy,Math.min(v.y+v.height-28/sy,handle.point.y));
    const outside=handle.point.x<v.x||handle.point.x>v.x+v.width||handle.point.y<v.y||handle.point.y>v.y+v.height;
    const name=P.familyTitle(handle.family,state.mode),g=make('g',{'data-guide-handle':handle.kind,role:'button',tabindex:0,'aria-label':`Move ${name}${outside?', off canvas':''}`});
    const title=make('title',{});title.textContent=`${name}: ${((handle.point.x-v.x)*100/v.width).toFixed(1)}%, ${((handle.point.y-v.y)*100/v.height).toFixed(1)}%. Drag or use arrow keys.`;g.append(title);
    g.append(make('circle',{cx:x,cy:y,r:10/Math.min(sx,sy),fill:'#ffffffde',stroke:'none'}),make('circle',{cx:x,cy:y,r:5/Math.min(sx,sy),fill:ink.color,stroke:ink.color,'stroke-width':1/sx,class:'point'}));
    const text=make('text',{x:Math.max(v.x+2/sx,Math.min(v.x+v.width-76/sx,x-18/sx)),y:y+20/sy,fill:ink.color,stroke:'#ffffffdd','stroke-width':3/sx,'paint-order':'stroke',style:`font-size:${11/sy}px`});text.textContent=name+(outside?' •':'');g.append(text);svg.append(g);
   }
   if(focus)svg.querySelector(`[data-guide-handle="${focus}"]`)?.focus({preventScroll:true});
  }
  svg.onpointerdown=e=>{
   if(e.button!==0)return;const target=e.target.closest('[data-guide-handle]');if(!target)return;
   e.preventDefault();e.stopPropagation();const state=getState();drag={id:e.pointerId,kind:target.dataset.guideHandle,start:point(e),settings:structuredClone(state),view:{...state.view}};
   target.focus({preventScroll:true});if(drag.kind!=='horizon')onSelect(drag.kind);svg.setPointerCapture(e.pointerId);
  };
  svg.onpointermove=e=>{if(!drag)return;const q=point(e);setSettings(geometry(P.moveGuide(drag.settings,drag.view,drag.kind,{x:q.x-drag.start.x,y:q.y-drag.start.y})));e.preventDefault();e.stopPropagation();};
  svg.onpointerup=e=>{if(!drag)return;svg.onpointermove(e);const id=drag.id;drag=null;if(svg.hasPointerCapture(id))svg.releasePointerCapture(id);};
  svg.onpointercancel=()=>cancel();svg.onlostpointercapture=()=>{if(drag)cancel();};
  svg.onkeydown=e=>{
   const target=e.target.closest('[data-guide-handle]');if(!target)return;
   if(e.key==='Escape'){cancel();e.preventDefault();e.stopPropagation();return;}
   const d={ArrowLeft:{x:-1,y:0},ArrowRight:{x:1,y:0},ArrowUp:{x:0,y:-1},ArrowDown:{x:0,y:1}}[e.key];if(!d)return;
   const state=getState(),step=e.shiftKey?10:1;setSettings(geometry(P.moveGuide(state,state.view,target.dataset.guideHandle,{x:d.x*step,y:d.y*step})));e.preventDefault();e.stopPropagation();
  };
  return{render,cancel,isDragging:()=>!!drag};
 };
})();
