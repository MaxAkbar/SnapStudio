(() => {
 'use strict';
 const P=window.snapPerspective,keys=['grid','center','left','right','top','bottom'],bit=key=>1<<keys.indexOf(key);
 const normalize=value=>value!==null&&value!==undefined&&String(value).trim()!==''&&Number.isInteger(Number(value))&&Number(value)>=0&&Number(value)<=63?Number(value):63;
 function read(el){let value=63;for(let current=el;current instanceof SVGElement;current=current.parentElement)if(current.hasAttribute('data-snap-targets'))value&=normalize(current.getAttribute('data-snap-targets'));return value;}
 function selection(elements){return elements.reduce((value,el)=>[el,...el.querySelectorAll('[data-snap-targets]')].filter(item=>!item.closest('defs,symbol,mask,clipPath,pattern,[data-snap-stroke-derived]')).reduce((value,item)=>value&read(item),value),63);}
 const allows=(value,family,mode)=>!!(value&(family===0||family===1?1:family===2?mode===1?2:4:family===3?8:family===4?16:family===5?12:family===6?48:family===7?2:0));
 P.targets={normalize,bit,read,selection,allows};
 window.createPerspectiveTargets=({selected,getMode,before,commit,status,render})=>{
  const $=id=>document.getElementById(id),boxes=new Map();let defaultTargets=63;
  function values(){return selected().length?selected().map(read):[defaultTargets];}
  function change(value,enabled=null){const old=before(),update=current=>enabled===null?value:enabled?current|value:current&~value;
   if(selected().length){for(const el of selected())el.setAttribute('data-snap-targets',update(read(el)));commit(old);}
   else{defaultTargets=update(defaultTargets);render();status('Perspective snap targets set for new drawings in this editor.');}
  }
  function refresh(){
   const mode=getMode(),current=values(),same=current.every(value=>value===current[0]);
   $('perspectiveTargetScope').textContent=selected().length?'Selected objects':'Default for new drawings';
   $('perspectiveTargetSummary').textContent='Perspective targets · '+(!same?'Mixed':current[0]===63?'All guides':current[0]===0?'None':P.activeFamilies(mode).filter(key=>current[0]&bit(key)).map(key=>P.familyTitle(key,mode)).join(', ')||'None in this view');
   for(const [key,{label,box,text}]of boxes){label.hidden=!P.activeFamilies(mode).includes(key);text.textContent=P.familyTitle(key,mode);box.setAttribute('aria-label','Snap to '+text.textContent);const states=current.map(value=>!!(value&bit(key)));box.checked=states.every(Boolean);box.indeterminate=states.some(Boolean)&&!states.every(Boolean);}
  }
  for(const key of keys){const label=document.createElement('label'),box=document.createElement('input'),text=document.createElement('span');label.className='check';box.type='checkbox';box.dataset.snapTarget=key;box.onchange=()=>change(bit(key),box.checked);label.append(box,text);$('perspectiveTargetList').append(label);boxes.set(key,{label,box,text});}
  $('perspectiveTargetsAll').onclick=()=>change(63);$('perspectiveTargetsNone').onclick=()=>change(0);
  return{refresh,default:()=>defaultTargets,current:()=>selected().length?selection(selected()):defaultTargets,setDefault:value=>{defaultTargets=normalize(value);refresh();}};
 };
})();
