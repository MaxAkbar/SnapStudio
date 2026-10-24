/* Standalone design prototype. No connection to SnapStudio files, settings, or database. */
'use strict';
const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];
const icon = (name, cls = '') => `<svg class="${cls}" aria-hidden="true"><use href="#i-${name}"/></svg>`;
const escapeHtml = value => String(value).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const app = $('#app');
const state = {folder:'website', page:0, size:10, search:'', activeCapture:1, tool:'move', zoom:1, fill:false, selectedLayer:'markup', selectedObjectIndex:0, layerId:3, currentColor:'#69c8ff', focus:false, customImage:false, transparent:false, undo:[], redo:[]};
const folders = {all:'All captures',unfiled:'Unfiled',website:'Website feedback',product:'Product shots',ideas:'Ideas & inspiration'};
const mappedFolders = {website:'Pictures / Website feedback',product:'Pictures / Product shots'};
const captureNames = [
  ['Homepage — hero feedback','website'],['Navigation exploration','website'],['Mobile landing page','website'],['Destination card details','website'],['Booking flow — step 2','website'],['Footer & newsletter','website'],['Search results layout','website'],['Homepage — first draft','website'],['Journey detail page','website'],['Account settings','website'],['Contact page','website'],['Pricing comparison','website'],['Saved destinations','website'],['Homepage — reference','website'],
  ['Desk setup — front view','product'],['Ceramic collection','product'],['Packaging detail','product'],['Studio lighting test','product'],['Desk setup — side view','product'],['Spring collection','product'],
  ['Typography inspiration','ideas'],['Color palette studies','ideas'],['Menu interaction idea','ideas'],['Moodboard — October','ideas'],
  ['Quick capture 08','unfiled'],['Meeting notes','unfiled'],['Design reference','unfiled'],['Quick capture 07','unfiled'],['Window capture','unfiled'],['Quick capture 06','unfiled'],['Release checklist','unfiled'],['New canvas','unfiled']
];
const captures = captureNames.map(([name,folder], i) => ({id:i+1,name,folder,meta:i<4?'Today':i<10?'Yesterday':'Oct 21',dimensions:i%3===1?'1440 × 900':'1120 × 740'}));
let layers = [
  {id:'markup',name:'Annotations',color:'#81cbfa',visible:true,expanded:true,kind:'rectangle',objects:[{name:'CTA highlight',kind:'rectangle'},{name:'Feedback note',kind:'text'}]},
  {id:'cursor',name:'Mouse pointer',color:'#d9b476',visible:true,expanded:false,kind:'move',objects:[{name:'Captured pointer',kind:'move'}]},
  {id:'background',name:'Original capture',color:'#6e7887',visible:true,expanded:false,locked:true,kind:'image',objects:[]}
];
let toastTimer;
function toast(message) {
  $('#toast span').textContent=message; $('#toast').classList.add('show');
  clearTimeout(toastTimer); toastTimer=setTimeout(()=>$('#toast').classList.remove('show'),4200);
  $('#status-text').textContent=message;
}
$('#close-toast').onclick=()=>$('#toast').classList.remove('show');
function closeMenus(){ $$('.popover').forEach(el=>el.hidden=true); $$('[aria-haspopup="menu"]').forEach(el=>el.setAttribute('aria-expanded','false')); }
function bindMenu(button,menu) {
  $(button).onclick=e=>{ e.stopPropagation(); const open=$(menu).hidden; closeMenus(); $(menu).hidden=!open; $(button).setAttribute('aria-expanded',String(open)); };
}
bindMenu('#capture-button','#capture-menu'); bindMenu('#export-button','#export-menu'); bindMenu('#sort-button','#sort-menu');
document.addEventListener('click', e=>{ if(!e.target.closest('.popover,.menu-anchor')) closeMenus(); });
$$('[data-demo]').forEach(button=>button.onclick=()=>{closeMenus();toast(`${button.dataset.demo} · preview only`);});
function filteredCaptures(){return captures.filter(c=>(state.folder==='all'||c.folder===state.folder)&&c.name.toLowerCase().includes(state.search.toLowerCase()));}
function renderCaptures(){
  const items=filteredCaptures(); const pages=Math.max(1,Math.ceil(items.length/state.size)); state.page=Math.min(state.page,pages-1);
  const start=state.page*state.size;
  $('#capture-list').innerHTML=items.slice(start,start+state.size).map(c=>`<div class="capture-card ${state.activeCapture===c.id?'selected':''}" data-capture="${c.id}" role="button" tabindex="0" aria-label="Open ${escapeHtml(c.name)}" aria-pressed="${state.activeCapture===c.id}"><div class="capture-thumb ${c.folder==='product'?'product':c.folder==='ideas'?'abstract':''}">${c.folder==='website'||c.folder==='unfiled'?'<img src="assets/sample-capture.svg" alt="">':''}</div><div class="capture-info"><span class="capture-name">${escapeHtml(c.name)}</span><span class="capture-meta">${c.meta}<span>·</span>${c.dimensions}</span></div><button class="card-more" aria-label="Manage ${escapeHtml(c.name)}" data-manage="${c.id}">···</button></div>`).join('')||'<div class="empty-state">No captures here yet.<br>Try another folder or search.</div>';
  $('#capture-count').textContent=items.length;
  $('#page-label').textContent=items.length?`${start+1}–${Math.min(start+state.size,items.length)} of ${items.length}`:'0 captures';
  $('#page-number').textContent=`${state.page+1} / ${pages}`;
  $('#previous-page').disabled=state.page===0; $('#next-page').disabled=state.page===pages-1;
  $$('.capture-card').forEach(card=>{
    card.onclick=e=>{if(e.target.closest('[data-manage]'))return;selectCapture(Number(card.dataset.capture));};
    card.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();selectCapture(Number(card.dataset.capture));}};
    card.oncontextmenu=e=>{e.preventDefault();showCaptureMenu(Number(card.dataset.capture),e.clientX,e.clientY);};
  });
  $$('[data-manage]').forEach(b=>b.onclick=e=>{e.stopPropagation();const r=b.getBoundingClientRect();showCaptureMenu(Number(b.dataset.manage),r.right,r.bottom);});
  renderFolderPicker();
}
function selectCapture(id){
  state.activeCapture=id;const capture=captures.find(c=>c.id===id);$('#document-title').textContent=capture.name;
  if(state.customImage)URL.revokeObjectURL($('#sample-image').src);
  state.customImage=false;state.transparent=false;$('#sample-image').src='assets/sample-capture.svg';$('#sample-image').hidden=false;$('#annotations').style.visibility='visible';$('#artboard-wrap').style.aspectRatio='1120/740';$('#canvas-label').nextElementSibling.textContent='1120 × 740';$('#background-label').textContent='Image';fitArtboard();
  $('#canvas-label').textContent=folders[capture.folder]||capture.folder;renderCaptures();
  toast(`Opened ${capture.name}`);
}
function renderFolderPicker(){
  const option=id=>{const count=captures.filter(c=>id==='all'||c.folder===id).length;return `<option value="${escapeHtml(id)}">${escapeHtml(folders[id])} (${count})</option>`;};
  const mapped=Object.keys(folders).filter(id=>mappedFolders[id]);
  const groups=Object.keys(folders).filter(id=>!['all','unfiled'].includes(id)&&!mappedFolders[id]);
  $('#folder-select').innerHTML=option('all')+option('unfiled')+(mapped.length?`<optgroup label="Mapped folders">${mapped.map(option).join('')}</optgroup>`:'')+(groups.length?`<optgroup label="Groups">${groups.map(option).join('')}</optgroup>`:'');
  $('#folder-select').value=state.folder;
  $('#folder-picker').title=mappedFolders[state.folder]?`Mapped folder · ${mappedFolders[state.folder]}`:folders[state.folder];
  $('#refresh-folders').disabled=!mappedFolders[state.folder]&&!['all','unfiled'].includes(state.folder);
  $('#refresh-folders').title=mappedFolders[state.folder]?'Refresh selected folder':['all','unfiled'].includes(state.folder)?'Refresh all mapped folders':'Select a mapped folder to refresh';
}
$('#folder-select').onchange=e=>selectFolder(e.target.value);
function selectFolder(id){
  state.folder=id;state.page=0;
  renderCaptures();
}
$('#search').oninput=e=>{state.search=e.target.value;state.page=0;renderCaptures();};
$('#previous-page').onclick=()=>{state.page--;renderCaptures();};$('#next-page').onclick=()=>{state.page++;renderCaptures();};
$$('[data-sort]').forEach(b=>b.onclick=()=>{captures.sort(b.dataset.sort==='name'?(a,b)=>a.name.localeCompare(b.name):(a,b)=>a.id-b.id);state.page=0;renderCaptures();closeMenus();toast(b.dataset.sort==='name'?'Sorted by name':'Newest captures first');});
function toggleLibrary(){if(innerWidth<=900){app.classList.toggle('library-mobile-open');}else{app.classList.toggle('library-collapsed');}requestAnimationFrame(fitArtboard);}
$('#collapse-library').onclick=toggleLibrary;$('#expand-library').onclick=toggleLibrary;$('#rail-folder').onclick=toggleLibrary;
function toggleInspector(){app.classList.toggle('inspector-collapsed');requestAnimationFrame(fitArtboard);}
$('#collapse-inspector').onclick=toggleInspector;$('#toggle-inspector').onclick=toggleInspector;
function toggleFocus(){state.focus=!state.focus;app.classList.toggle('focus-mode',state.focus);$('#focus-button').setAttribute('aria-pressed',String(state.focus));requestAnimationFrame(fitArtboard);}
$('#focus-button').onclick=toggleFocus;
$$('[data-section]').forEach(b=>b.onclick=()=>{
  const names={captures:'capture-section',properties:'properties-section',layers:'layers-section',image:'image-section'};
  const content=$(`#${names[b.dataset.section]}`);content.hidden=!content.hidden;b.setAttribute('aria-expanded',String(!content.hidden));
});
const toolDefinitions=[['move','Move','V'],['select','Selection','M'],null,['rectangle','Rectangle','U'],['ellipse','Ellipse','O'],['line','Line','L'],['arrow','Arrow','A'],null,['text','Text','T'],['pen','Pen','P'],['highlight','Highlight','H'],['blur','Blur','B'],['fill','Fill bucket','G'],null,['crop','Crop','C']];
$('#tool-rail').innerHTML=toolDefinitions.map(t=>t?`<button class="tool-button ${t[0]==='move'?'active':''}" data-tool="${t[0]}" title="${t[1]} (${t[2]})" aria-label="${t[1]} tool" aria-pressed="${t[0]==='move'}">${icon(t[0])}<span class="tool-shortcut">${t[2]}</span></button>`:'<span class="tool-separator"></span>').join('');
function setTool(tool){
  state.tool=tool;const name=toolDefinitions.find(t=>t&&t[0]===tool)[1];
  $$('[data-tool]').forEach(b=>{b.classList.toggle('active',b.dataset.tool===tool);b.setAttribute('aria-pressed',String(b.dataset.tool===tool));});
  $('#active-tool-name').textContent=name;$('#active-tool-icon use').setAttribute('href',`#i-${tool}`);
  const hints={move:'Select an object to move or edit it',select:'Drag a marquee to select objects',fill:'Click the highlighted shape to fill it',crop:'Drag a selection to preview a crop',text:'Click the canvas to add a note',pen:'Drag on the canvas to draw'};
  $('#context-hint').textContent=hints[tool]||`Drag on the canvas to draw a ${name.toLowerCase()}`;
  $('#canvas-tip').textContent=tool==='move'?'Drag objects to move · Ctrl + scroll to zoom':hints[tool]||`Drag to draw · Ctrl + scroll to zoom`;
  $('#annotations').style.cursor=tool==='move'?'default':tool==='text'?'text':'crosshair';
}
$$('[data-tool]').forEach(b=>b.onclick=()=>setTool(b.dataset.tool));
function snapshot(){return {markup:$('#annotations').innerHTML,color:state.currentColor,width:$('#stroke-width').value,fill:state.fill};}
function pushUndo(){state.undo.push(snapshot());state.redo=[];}
function restoreSnapshot(s){$('#annotations').innerHTML=s.markup;state.currentColor=s.color;$('#stroke-width').value=s.width;state.fill=s.fill;$('#stroke-color').value=s.color;$('#stroke-hex').textContent=s.color.slice(1).toUpperCase();}
function undo(){if(!state.undo.length){toast('No changes to undo yet');return;}state.redo.push(snapshot());restoreSnapshot(state.undo.pop());toast('Change undone');}
function redo(){if(!state.redo.length){toast('No changes to redo yet');return;}state.undo.push(snapshot());restoreSnapshot(state.redo.pop());toast('Change restored');}
$('#undo-button').onclick=undo;$('#redo-button').onclick=redo;
function setColor(color,record=true){
  if(record)pushUndo();state.currentColor=color;$('#stroke-color').value=color;$('#stroke-hex').textContent=color.slice(1).toUpperCase();
  $('#annotation-shape').setAttribute('stroke',color);if(state.fill)$('#annotation-shape').setAttribute('fill',`${color}40`);
  $$('[data-color]').forEach(b=>b.classList.toggle('active',b.dataset.color===color));
}
$('#stroke-color').oninput=e=>setColor(e.target.value,false);$('#stroke-color').onchange=()=>toast('Stroke color updated');
$$('[data-color]').forEach(b=>b.onclick=()=>setColor(b.dataset.color));$('#custom-color').onclick=()=>$('#stroke-color').click();
$('#stroke-width').oninput=e=>{const width=Number(e.target.value);if(Number.isFinite(width)&&width>0)$('#annotation-shape').setAttribute('stroke-width',width);};
$('#opacity').oninput=e=>{$('#opacity-value').textContent=`${e.target.value}%`;$('#annotation-shape').style.opacity=e.target.value/100;};
$('#fill-toggle').onclick=()=>{pushUndo();state.fill=!state.fill;$('#annotation-shape').setAttribute('fill',state.fill?`${state.currentColor}40`:'none');$('#fill-toggle').setAttribute('aria-pressed',String(state.fill));$('#fill-label').textContent=state.fill?'Color · 25%':'None';};
function corners(round){pushUndo();$('#annotation-shape').setAttribute('rx',round?'10':'0');$('#corners-round').classList.toggle('active',round);$('#corners-square').classList.toggle('active',!round);}
$('#corners-round').onclick=()=>corners(true);$('#corners-square').onclick=()=>corners(false);
$('#object-name').oninput=e=>{const layer=layers.find(l=>l.id===state.selectedLayer);const object=layer?.objects[state.selectedObjectIndex];if(object){object.name=e.target.value;if(state.selectedLayer==='markup'&&state.selectedObjectIndex===0){const label=$('#selection-bounds text');if(label)label.textContent=e.target.value;}}else if(layer){layer.name=e.target.value;}renderLayers();};
$('#font-family').onchange=e=>$('#annotation-note text:last-of-type').style.fontFamily=e.target.value;
$('#font-size').oninput=e=>{if(Number(e.target.value)>0)$('#annotation-note text:last-of-type').setAttribute('font-size',e.target.value);};
$('#note-text').oninput=e=>$('#annotation-note text:last-of-type').textContent=e.target.value;
function renderLayers(){
  $('#layer-count').textContent=layers.length;
  $('#layer-list').innerHTML=layers.map(l=>`<div class="layer-row ${state.selectedLayer===l.id?'selected':''} ${!l.expanded?'children-collapsed':''} ${!l.visible?'muted':''}" style="--layer-color:${l.color}" data-layer="${l.id}"><div class="layer-main" role="button" tabindex="0" aria-label="Select layer ${escapeHtml(l.name)}" aria-pressed="${state.selectedLayer===l.id}"><button class="icon-button eye-button" data-eye="${l.id}" title="${l.visible?'Hide':'Show'} ${escapeHtml(l.name)}" aria-label="${l.visible?'Hide':'Show'} ${escapeHtml(l.name)}">${icon(l.visible?'eye':'eye-off')}</button><button class="disclosure-button" data-expand="${l.id}" aria-label="Toggle objects in ${escapeHtml(l.name)}" aria-expanded="${l.expanded}">${icon('chevron')}</button><span class="layer-preview">${l.kind==='image'?'<img src="assets/sample-capture.svg" alt="">':icon(l.kind)}</span><span class="layer-name"><strong>${escapeHtml(l.name)}</strong><span>${l.locked?'Background image':`${l.objects.length} object${l.objects.length===1?'':'s'}`}</span></span>${l.locked?icon('lock','lock-icon'):''}</div>${l.objects.map((o,i)=>`<button class="object-row ${i===0&&state.selectedLayer===l.id?'active':''}" data-object="${l.id}" data-object-index="${i}">${icon(o.kind)}<span>${escapeHtml(o.name)}</span></button>`).join('')}</div>`).join('');
  $$('[data-layer]').forEach(row=>{
    row.querySelector('.layer-main').onclick=e=>{if(e.target.closest('button'))return;selectLayer(row.dataset.layer);};
    row.querySelector('.layer-main').onkeydown=e=>{if(e.target===e.currentTarget&&(e.key==='Enter'||e.key===' ')){e.preventDefault();selectLayer(row.dataset.layer);}};
    row.oncontextmenu=e=>{e.preventDefault();showLayerMenu(row.dataset.layer,e.clientX,e.clientY);};
  });
  $$('[data-eye]').forEach(b=>b.onclick=()=>{const l=layers.find(l=>l.id===b.dataset.eye);l.visible=!l.visible;syncLayerVisibility();renderLayers();});
  $$('[data-expand]').forEach(b=>b.onclick=()=>{const l=layers.find(l=>l.id===b.dataset.expand);l.expanded=!l.expanded;renderLayers();});
  $$('[data-object]').forEach(b=>{b.classList.toggle('active',b.dataset.object===state.selectedLayer&&Number(b.dataset.objectIndex)===state.selectedObjectIndex);b.onclick=()=>{selectLayer(b.dataset.object,Number(b.dataset.objectIndex));};});
}
function selectLayer(id,index=0){
  state.selectedLayer=id;state.selectedObjectIndex=index;const l=layers.find(l=>l.id===id);const o=l.objects[index];
  $('#object-name').value=o?.name||l.name;$('#object-type').textContent=o?o.kind[0].toUpperCase()+o.kind.slice(1):'Image';
  $('#shape-properties').hidden=o?.kind!=='rectangle';$('#text-properties').hidden=o?.kind!=='text';
  $('#selection-bounds').style.display=id==='markup'&&index===0?'':'none';renderLayers();
}
function syncLayerVisibility(){
  const markup=layers.find(l=>l.id==='markup');$('#annotation-shape').style.display=markup?.visible?'':'none';$('#annotation-note').style.display=markup?.visible?'':'none';$('#selection-bounds').style.display=markup?.visible&&state.selectedLayer==='markup'&&state.selectedObjectIndex===0?'':'none';
  $('#sample-image').style.visibility=layers.find(l=>l.id==='background')?.visible?'visible':'hidden';
  const cursor=$('#sample-cursor');if(cursor)cursor.style.display=layers.find(l=>l.id==='cursor')?.visible?'':'none';
}
$('#add-layer').onclick=()=>{const l={id:`layer-${++state.layerId}`,name:`Layer ${state.layerId}`,color:'#b8a4e6',visible:true,expanded:true,kind:'layers',objects:[]};layers.unshift(l);selectLayer(l.id);toast('New empty layer added');};
function showContext(items,x,y){closeMenus();const menu=$('#context-menu');menu.innerHTML=items.map((item,i)=>item==='divider'?'<div class="menu-divider"></div>':`<button role="menuitem" data-context-index="${i}" class="${item.danger?'danger':''}">${icon(item.icon||'chevron')}<span>${escapeHtml(item.label)}</span></button>`).join('');menu.hidden=false;const r=menu.getBoundingClientRect();menu.style.left=`${Math.max(8,Math.min(x,innerWidth-r.width-8))}px`;menu.style.top=`${Math.max(8,Math.min(y,innerHeight-r.height-36))}px`;menu.querySelectorAll('button').forEach(b=>b.onclick=()=>{menu.hidden=true;items[Number(b.dataset.contextIndex)].action();});}
function showLayerMenu(id,x,y){const layer=layers.find(l=>l.id===id);selectLayer(id);showContext([
  {label:'Rename layer…',icon:'pen',action:()=>openForm({title:'Rename layer',description:'Give this layer a name that is easy to find.',label:'Layer name',value:layer.name,submit:'Rename',onSubmit:value=>{layer.name=value;renderLayers();}})},
  {label:'Duplicate layer',icon:'copy',action:()=>{const copy=structuredClone(layer);copy.id=`layer-${++state.layerId}`;copy.name+=' copy';copy.locked=false;layers.splice(layers.indexOf(layer),0,copy);selectLayer(copy.id);toast('Layer duplicated in preview');}},
  'divider',
  {label:'Move up',icon:'up',action:()=>{const i=layers.indexOf(layer);if(i>0){[layers[i-1],layers[i]]=[layers[i],layers[i-1]];renderLayers();}}},
  {label:'Move down',icon:'down',action:()=>{const i=layers.indexOf(layer);if(i<layers.length-1){[layers[i+1],layers[i]]=[layers[i],layers[i+1]];renderLayers();}}},
  {label:'Custom color…',icon:'fill',action:()=>{openDialog({title:'Layer color',description:'A subtle color label helps you find related layers.',content:`<label class="dialog-field"><span>Label color</span><input type="color" id="dialog-color" value="${layer.color}"></label>`,submit:'Apply color',onSubmit:()=>{layer.color=$('#dialog-color').value;renderLayers();}});}},
  ...(layer.locked?[]:['divider',{label:'Delete layer',icon:'trash',danger:true,action:()=>{layers=layers.filter(l=>l.id!==id);selectLayer(layers[0].id);syncLayerVisibility();toast('Layer removed from preview');}}])
],x,y);}
function showCaptureMenu(id,x,y){const c=captures.find(c=>c.id===id);if(!c){toast('Choose a capture from the library first');return;}showContext([
  {label:'Rename…',icon:'pen',action:()=>openForm({title:'Rename capture',description:'Make this capture easier to find in your library.',label:'Capture name',value:c.name,submit:'Rename',onSubmit:value=>{c.name=value;if(state.activeCapture===id)$('#document-title').textContent=value;renderCaptures();}})},
  {label:'Duplicate',icon:'copy',action:()=>{captures.unshift({...c,id:Math.max(...captures.map(c=>c.id))+1,name:c.name+' copy'});renderCaptures();toast('Capture duplicated in preview');}},
  {label:'Move to folder…',icon:'folder',action:()=>openDialog({title:'Move capture',description:c.name,content:`<label class="dialog-field"><span>Destination folder</span><select id="dialog-destination">${Object.entries(folders).filter(([id])=>id!=='all').map(([id,n])=>`<option value="${id}" ${c.folder===id?'selected':''}>${escapeHtml(n)}</option>`).join('')}</select></label>`,submit:'Move capture',onSubmit:()=>{c.folder=$('#dialog-destination').value;renderCaptures();toast('Capture moved');}})},
  {label:'Show in folder',icon:'folder',action:()=>toast('Reveal this capture in File Explorer · preview only')},'divider',
  {label:'Remove from library',icon:'trash',danger:true,action:()=>{captures.splice(captures.indexOf(c),1);if(state.activeCapture===id){const next=filteredCaptures()[0]||captures[0];if(next)selectCapture(next.id);else state.activeCapture=null;}renderCaptures();toast('Removed from this preview library');}}
],x,y);}
$('#document-menu-button').onclick=e=>{e.stopPropagation();const r=e.currentTarget.getBoundingClientRect();showCaptureMenu(state.activeCapture,r.right-210,r.bottom+5);};
function openDialog({title,description,content,submit,onSubmit}){
  closeMenus();$('#dialog-title').textContent=title;$('#dialog-description').textContent=description;$('#dialog-content').innerHTML=content;$('#dialog-submit').textContent=submit;const dialog=$('#app-dialog');dialog.returnValue='';dialog.onclose=()=>{if(dialog.returnValue==='confirm')onSubmit();};dialog.showModal();
}
function openForm({title,description,label,value,submit,onSubmit}){openDialog({title,description,content:`<label class="dialog-field"><span>${escapeHtml(label)}</span><input id="dialog-value" value="${escapeHtml(value)}" required maxlength="60" autofocus></label>`,submit,onSubmit:()=>onSubmit($('#dialog-value').value.trim())});}
function addFolder(mapped=true){
  openDialog({
    title:mapped?'Add a folder':'Create a group',
    description:mapped?'Keep editable copies together. Refresh will bring in new images while your originals stay untouched.':'Organize related captures into a group in your library.',
    content:`<label class="dialog-field"><span>${mapped?'Folder name':'Group name'}</span><input id="folder-name" placeholder="e.g. Client project" required maxlength="60" autofocus></label>${mapped?'<label class="dialog-field"><span>Folder location</span><input id="folder-location" value="Pictures / Client project" aria-label="Example folder location"></label><label class="dialog-check"><input type="checkbox" checked> Include images in subfolders</label>':''}`,
    submit:mapped?'Add folder':'Create group',
    onSubmit:()=>{
      const name=$('#folder-name').value.trim();const id=`group-${Date.now()}`;
      folders[id]=name;if(mapped)mappedFolders[id]=$('#folder-location').value.trim()||name;
      selectFolder(id);toast(`${name} added to the preview`);
    }
  });
}
$('#add-folder').onclick=()=>addFolder();$('#new-group').onclick=()=>addFolder(false);
$('#refresh-folders').onclick=()=>{const b=$('#refresh-folders');b.animate([{transform:'rotate(0)'},{transform:'rotate(360deg)'}],{duration:600});toast('Folders are up to date · no new images in this sample');};
$('#settings-button').onclick=()=>openDialog({title:'Make room for your work',description:'Try the workspace controls in this design preview.',content:'<div class="property-row"><span>Library</span><span>Folders & paged captures</span></div><div class="property-row"><span>Inspector</span><span>Object settings & layers</span></div><div class="property-row"><span>Focus mode</span><span>Click the corner-frame icon</span></div><div class="property-row"><span>Layers</span><span>Right-click for more options</span></div>',submit:'Got it',onSubmit:()=>{}});
$('#new-canvas').onclick=()=>openDialog({title:'A fresh canvas',description:'Start with a transparent background, ready for whatever comes next.',content:'<div class="dialog-split"><label class="dialog-field"><span>Width</span><input type="number" value="1120" min="1" max="4096" id="new-width" required></label><label class="dialog-field"><span>Height</span><input type="number" value="740" min="1" max="4096" id="new-height" required></label></div><label class="dialog-check"><input type="checkbox" checked disabled> Transparent background</label>',submit:'Create canvas',onSubmit:()=>{state.transparent=true;$('#sample-image').hidden=true;$('#annotations').style.visibility='hidden';$('#background-label').textContent='Transparent';$('#document-title').textContent='Untitled canvas';const w=$('#new-width').value,h=$('#new-height').value;$('#artboard-wrap').style.aspectRatio=`${w}/${h}`;$('#canvas-label').textContent='New transparent canvas';$('#canvas-label+span');$('#canvas-label').nextElementSibling.textContent=`${w} × ${h}`;fitArtboard();toast('Transparent canvas created in preview');}});
$('#open-image').onclick=()=>$('#image-input').click();$('#image-input').onchange=e=>{const file=e.target.files[0];if(!file)return;if(!file.type.startsWith('image/'))return toast('Choose an image file');if(state.customImage)URL.revokeObjectURL($('#sample-image').src);const url=URL.createObjectURL(file);state.customImage=true;state.transparent=false;const img=$('#sample-image');img.src=url;img.hidden=false;$('#annotations').style.visibility='hidden';img.onload=()=>{$('#artboard-wrap').style.aspectRatio=`${img.naturalWidth}/${img.naturalHeight}`;$('#document-title').textContent=file.name;$('#canvas-label').textContent='Local preview';$('#canvas-label').nextElementSibling.textContent=`${img.naturalWidth} × ${img.naturalHeight}`;fitArtboard();};toast('Image opened locally in the preview');};
$('#paste-image').onclick=()=>openDialog({title:'Paste an image',description:'Choose where the pasted image should go.',content:'<label class="dialog-check"><input type="radio" name="paste-mode" value="layer" checked> As a layer in the current image</label><label class="dialog-check"><input type="radio" name="paste-mode" value="image"> As a new image</label>',submit:'Preview paste',onSubmit:()=>{const layer=$('input[name="paste-mode"]:checked').value==='layer';if(layer){$('#add-layer').click();layers[0].name='Pasted image';layers[0].kind='image';renderLayers();}toast(layer?'Pasted image layer added · sample':'New image paste selected · sample');}});
$('#copy-button').onclick=()=>toast('Copy the visible image and annotations · preview only');
$$('[data-export]').forEach(b=>b.onclick=()=>{closeMenus();if(b.dataset.export!=='svg'){toast(`Save as ${b.dataset.export.toUpperCase()} selected · preview only`);return;}const a=document.createElement('a');a.href='assets/sample-capture.svg';a.download='snapstudio-sample-capture.svg';a.click();toast('Sample artwork downloaded');});
function fitArtboard(){
  const area=$('#canvas-area'),wrap=$('#artboard-wrap');const style=getComputedStyle(area);const width=area.clientWidth-parseFloat(style.paddingLeft)-parseFloat(style.paddingRight);const height=area.clientHeight-parseFloat(style.paddingTop)-parseFloat(style.paddingBottom);const ratio=parseFloat(wrap.style.aspectRatio?.split('/')[0])/parseFloat(wrap.style.aspectRatio?.split('/')[1])||1120/740;wrap.style.width=`${Math.max(60,Math.min(width,height*ratio))}px`;
}
new ResizeObserver(fitArtboard).observe($('#canvas-area'));
function zoom(value){state.zoom=Math.max(.4,Math.min(3,value));$('#artboard-wrap').style.transform=`scale(${state.zoom})`;$('#zoom-reset').textContent=state.zoom===1?'Fit':`${Math.round(state.zoom*100)}%`;}
$('#zoom-in').onclick=()=>zoom(state.zoom+.15);$('#zoom-out').onclick=()=>zoom(state.zoom-.15);$('#zoom-reset').onclick=()=>zoom(1);$('#zoom-fit').onclick=()=>zoom(1);
$('#canvas-area').addEventListener('wheel',e=>{if(e.ctrlKey){e.preventDefault();zoom(state.zoom+(e.deltaY<0?.08:-.08));}},{passive:false});
// A few real editing gestures make the layout review more tangible.
const svgNS='http://www.w3.org/2000/svg';
const svg=$('#annotations');let gesture;
function canvasPoint(e){const r=svg.getBoundingClientRect();return{x:(e.clientX-r.left)*1120/r.width,y:(e.clientY-r.top)*740/r.height};}
function createSvg(name,attrs){const el=document.createElementNS(svgNS,name);Object.entries(attrs).forEach(([k,v])=>el.setAttribute(k,v));svg.appendChild(el);return el;}
svg.addEventListener('pointerdown',e=>{
  if(e.button!==0)return;const p=canvasPoint(e);
  if(state.tool==='fill'){if(e.target.id==='annotation-shape')$('#fill-toggle').click();return;}
  if(state.tool==='text'){pushUndo();const text=createSvg('text',{x:p.x,y:p.y,fill:state.currentColor,'font-size':24,'font-weight':600});text.textContent='Your note here';toast('Sample note added');return;}
  if(state.tool==='move'){
    if(e.target.id!=='annotation-shape')return;pushUndo();selectLayer('markup');const bounds=$('#selection-bounds').transform.baseVal.consolidate()?.matrix;gesture={kind:'move',start:p,x:Number(e.target.getAttribute('x')),y:Number(e.target.getAttribute('y')),boundsX:bounds?.e||0,boundsY:bounds?.f||0};
  }else{
    pushUndo();const kind=state.tool;let el;
    const attrs={stroke:state.currentColor,'stroke-width':$('#stroke-width').value,fill:'none','stroke-linecap':'round','stroke-linejoin':'round'};
    if(kind==='ellipse')el=createSvg('ellipse',{...attrs,cx:p.x,cy:p.y,rx:0,ry:0});
    else if(['line','arrow'].includes(kind))el=createSvg('path',{...attrs,d:`M${p.x} ${p.y}`,...(kind==='arrow'?{'marker-end':'url(#arrowhead)'}:{})});
    else if(kind==='pen')el=createSvg('path',{...attrs,d:`M${p.x} ${p.y}`});
    else el=createSvg('rect',{...attrs,x:p.x,y:p.y,width:0,height:0,...(['select','crop'].includes(kind)?{'stroke-dasharray':'6 4','stroke-width':1.5}:kind==='highlight'?{fill:'#f2d38166',stroke:'none'}:kind==='blur'?{fill:'#8fa5bb99',stroke:'none'}:{})});
    gesture={kind,start:p,el,path:`M${p.x} ${p.y}`};
  }
  svg.setPointerCapture(e.pointerId);e.preventDefault();
});
svg.addEventListener('pointermove',e=>{if(!gesture)return;const p=canvasPoint(e),g=gesture;
  if(g.kind==='move'){const dx=p.x-g.start.x,dy=p.y-g.start.y;$('#annotation-shape').setAttribute('x',g.x+dx);$('#annotation-shape').setAttribute('y',g.y+dy);$('#selection-bounds').setAttribute('transform',`translate(${g.boundsX+dx} ${g.boundsY+dy})`);}
  else if(g.kind==='ellipse'){g.el.setAttribute('rx',Math.abs(p.x-g.start.x));g.el.setAttribute('ry',Math.abs(p.y-g.start.y));}
  else if(['line','arrow'].includes(g.kind)){g.el.setAttribute('d',`M${g.start.x} ${g.start.y}L${p.x} ${p.y}`);}
  else if(g.kind==='pen'){g.path+=`L${p.x} ${p.y}`;g.el.setAttribute('d',g.path);}
  else{g.el.setAttribute('x',Math.min(g.start.x,p.x));g.el.setAttribute('y',Math.min(g.start.y,p.y));g.el.setAttribute('width',Math.abs(p.x-g.start.x));g.el.setAttribute('height',Math.abs(p.y-g.start.y));}
});
svg.addEventListener('pointerup',()=>{if(gesture){if(gesture.kind==='move')toast('Object moved');else if(['select','crop'].includes(gesture.kind))toast('Selection stays visible while you choose the next action');else toast('Sample annotation added');gesture=null;}});
svg.addEventListener('pointercancel',()=>{gesture=null;});
document.addEventListener('keydown',e=>{
  if(e.key==='Escape'){closeMenus();$('#toast').classList.remove('show');if(state.focus)toggleFocus();return;}
  if(e.target.closest('input,textarea,select,dialog'))return;
  if(e.key==='F8'){e.preventDefault();toggleFocus();return;}
  if(e.ctrlKey&&e.key.toLowerCase()==='z'){e.preventDefault();undo();return;}
  if(e.ctrlKey&&e.key.toLowerCase()==='y'){e.preventDefault();redo();return;}
  if(!e.ctrlKey&&!e.metaKey&&!e.altKey){const tool=toolDefinitions.find(t=>t&&t[2].toLowerCase()===e.key.toLowerCase());if(tool){e.preventDefault();setTool(tool[0]);}}
});
// Captured cursor is a separate object in the sample layer stack.
createSvg('path',{id:'sample-cursor',d:'M925 417l2 28 7-8 8 12 6-4-8-11 11-2Z',fill:'#fff',stroke:'#243d4e','stroke-width':1.5});
renderCaptures();renderLayers();fitArtboard();
