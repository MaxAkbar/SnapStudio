(() => {
 'use strict';
 window.createSvgObjectMenu=({canAction,onAction,onCanvasContext,onListContext})=>{
  const $=id=>document.getElementById(id),menu=$('gridContextMenu'),stage=$('stage'),layers=$('layers'),triggers=[$('objectEditToggle'),$('objectMoreToggle')];
  let restoreFocus=()=>stage;
  const items=()=>[...menu.querySelectorAll('[role=menuitem]:not(:disabled)')];
  function refresh(){document.querySelectorAll('[data-action]').forEach(button=>{button.disabled=!canAction(button.dataset.action);});}
  function close(restore=false){const wasOpen=!menu.hidden;menu.hidden=true;triggers.forEach(button=>button.setAttribute('aria-expanded','false'));if(restore&&wasOpen)(restoreFocus()||stage).focus({preventScroll:true});}
  function open(p,focus=()=>stage,trigger=null){
   close();refresh();restoreFocus=focus;menu.hidden=false;triggers.forEach(button=>button.setAttribute('aria-expanded',String(button===trigger)));
   const rect=menu.getBoundingClientRect();menu.style.left=`${Math.max(6,Math.min(p.x,innerWidth-rect.width-6))}px`;menu.style.top=`${Math.max(6,Math.min(p.y,innerHeight-rect.height-6))}px`;
   items()[0]?.focus({preventScroll:true});
  }
  for(const trigger of triggers)trigger.onclick=()=>{if(!menu.hidden&&trigger.getAttribute('aria-expanded')==='true'){close(true);return;}const r=trigger.getBoundingClientRect();open({x:r.left,y:r.bottom+4},()=>trigger,trigger);};
  stage.addEventListener('contextmenu',e=>{e.preventDefault();onCanvasContext(e);open({x:e.clientX,y:e.clientY});});
  layers.addEventListener('contextmenu',e=>{const row=e.target.closest('[data-object-index]');if(!row)return;e.preventDefault();const focus=onListContext(row);open({x:e.clientX,y:e.clientY},focus);});
  for(const surface of [stage,layers])surface.addEventListener('keydown',e=>{if(e.key!=='ContextMenu'&&!(e.shiftKey&&e.key==='F10'))return;e.preventDefault();e.stopPropagation();const row=e.target.closest('[data-object-index]'),focus=row?onListContext(row):()=>stage,r=(focus()||stage).getBoundingClientRect();open({x:r.left+r.width/2,y:r.top+r.height/2},focus);});
  menu.addEventListener('click',e=>{const button=e.target.closest('[data-action]');if(!button||button.disabled)return;close(true);onAction(button.dataset.action);});
  menu.addEventListener('keydown',e=>{
   if((e.ctrlKey||e.metaKey)&&'zydsacxvg[]{}'.includes(e.key.toLowerCase())||e.key==='Delete'||e.key==='Backspace')return;
   e.stopPropagation();const enabled=items(),index=enabled.indexOf(document.activeElement);
   if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)){e.preventDefault();const next=e.key==='Home'?0:e.key==='End'?enabled.length-1:(index+(e.key==='ArrowUp'?-1:1)+enabled.length)%enabled.length;enabled[next]?.focus({preventScroll:false});}
   else if(e.key.length===1&&!e.ctrlKey&&!e.metaKey&&!e.altKey){const next=[...enabled.slice(index+1),...enabled.slice(0,index+1)].find(item=>item.textContent.trim().toLowerCase().startsWith(e.key.toLowerCase()));if(next){e.preventDefault();next.focus();}}
  });
  document.addEventListener('pointerdown',e=>{if(!menu.hidden&&!menu.contains(e.target)&&!triggers.some(button=>button.contains(e.target)))close();});
  document.addEventListener('keydown',e=>{if(menu.hidden)return;if(e.key==='Escape'){e.preventDefault();e.stopImmediatePropagation();close(true);}else if(e.key==='Tab')close(true);},true);
  window.addEventListener('resize',()=>close());document.addEventListener('scroll',e=>{if(!menu.contains(e.target))close();},true);
  return{close,refresh};
 };
})();
