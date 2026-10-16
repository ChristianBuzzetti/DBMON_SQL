'use strict';
// Console dock adapter. The original engine adopts [data-boberto-dock]; this file only bridges theme, size,
// Studio requests and look changes. No SQL, credentials or remote resources cross the bridge.
(function(){
 const post=message=>window.chrome?.webview?.postMessage(message);
 const dock=document.getElementById('dock');
 // The dock opens the native Personalization window rather than an in-page modal sized to the rail.
 window.BOBERTO_ESTUDIO={abrir(){post({type:'studio'});return true;}};
 // Studio runs in another WebView of the same profile and origin: its setters write smg_* keys, which arrive here
 // as storage events. Reloading re-reads every axis through the engine's own loader instead of mirroring setters.
 // A short settle (one choice writes several keys) keeps it near real time; Studio's choice cooldown spaces choices,
 // and reloads stay at least a second apart so dragging the opacity slider cannot chain them.
 let reloadTimer;
 window.addEventListener('storage',event=>{
  if(event.key!==null&&!/^smg_(boberto|mascota)_/.test(event.key))return;
  clearTimeout(reloadTimer);reloadTimer=setTimeout(()=>location.reload(),Math.max(150,1000-performance.now()));
 });
 // The engine re-measures on ResizeObserver. A one-pixel nudge makes it read a new data-boberto-escala-max now.
 function remeasure(){dock.style.marginRight='1px';requestAnimationFrame(()=>requestAnimationFrame(()=>{dock.style.marginRight='';}));}
 // The header band is short: the companion size is a ceiling, and the band's height decides the scale that fits
 // canvas (21 units), floor and reading (about 3.5 units plus 26 px of padding and type) without clipping.
 let requestedScale=Number(dock.getAttribute('data-boberto-escala-max'))||4.2;
 function fitScale(){const h=document.documentElement.clientHeight;if(!h)return requestedScale;return Math.max(1.4,Math.min(requestedScale,Math.floor(((h-28)/24.5)*10)/10));}
 // The estimate cannot know what a frame, a tall accessory or the reading add, so the result is measured: while the
 // canvas top sits above the safety margin the scale steps down, a few passes at most, until Boberto fits whole.
 const SAFE_TOP=4;let fitPass=0,fitTimer;
 function correctFit(){
  const canvas=dock.querySelector('#plt-wrap canvas');if(!canvas||!canvas.width)return;
  const top=canvas.getBoundingClientRect().top,current=Number(dock.getAttribute('data-boberto-escala-max'))||requestedScale;
  if(top>=SAFE_TOP||current<=1.4||fitPass>=8)return;
  fitPass++;
  const next=Math.max(1.4,Math.floor((current-(SAFE_TOP-top)/21-0.1)*10)/10);
  dock.setAttribute('data-boberto-escala-max',String(next));remeasure();
  clearTimeout(fitTimer);fitTimer=setTimeout(correctFit,140);
 }
 function applyScale(){
  fitPass=0;const next=String(fitScale());
  if(dock.getAttribute('data-boberto-escala-max')!==next){dock.setAttribute('data-boberto-escala-max',next);remeasure();}
  clearTimeout(fitTimer);fitTimer=setTimeout(correctFit,160);
 }
 window.addEventListener('resize',applyScale,{passive:true});
 // A frame chosen in Studio raises the character (more floor clearance): fit again when it changes.
 new MutationObserver(applyScale).observe(dock,{attributes:true,attributeFilter:['data-boberto-borde']});
 window.desktopDock={
  applyTheme(colors,theme,scale,motion){
   const root=document.documentElement;
   for(const [name,value] of Object.entries(colors))root.style.setProperty(name,value);
   root.setAttribute('data-theme',theme);root.setAttribute('data-animations',motion?'on':'off');
   requestedScale=Number(scale)||requestedScale;applyScale();
   document.dispatchEvent(new Event('smg:theme-preview'));
  },
  facts(){
   const B=window.BOBERTO,canvas=dock.querySelector('#plt-wrap canvas'),layer=dock.querySelector('.plt-dock-fondo image');
   return {ready:!!B&&!!B.dock(),adopted:!!canvas&&canvas.width>0,estado:dock.getAttribute('data-boberto-estado'),fondo:dock.getAttribute('data-boberto-fondo'),fondoImage:layer?layer.getAttribute('href'):null,borde:dock.getAttribute('data-boberto-borde'),hat:B.getHat(),character:typeof B.getCharacter==='function'?B.getCharacter():null,reading:(dock.querySelector('.plt-dock-rotulo')||{}).textContent||'',label:dock.getAttribute('aria-label'),scale:parseFloat(getComputedStyle(dock).getPropertyValue('--plt-dock-u'))||0,width:dock.clientWidth,slotHeight:dock.offsetHeight,viewportHeight:document.documentElement.clientHeight,requested:requestedScale,canvasTop:canvas?Math.round(canvas.getBoundingClientRect().top):null,floorGap:(()=>{const floor=dock.querySelector('.plt-dock-piso');return floor?Math.round(document.documentElement.clientHeight-floor.getBoundingClientRect().bottom):null;})(),ambient:document.documentElement.hasAttribute('data-ambient')};
  }
 };
 // The engine paints the background into .plt-dock-fondo; the rail reuses the same packaged image as ambient light.
 const sky=document.querySelector('.dock-sky');
 function syncAmbient(){
  const image=dock.querySelector('.plt-dock-fondo image'),href=image?image.getAttribute('href'):'';
  if(href&&/^https:\/\/boberto\.desktop\.invalid\/assets\/fondo-[a-z0-9-]+\.jpg(\?\d+)?$/.test(href)){sky.style.setProperty('--dock-ambient','url("'+href+'")');document.documentElement.setAttribute('data-ambient','');}
  else{sky.style.removeProperty('--dock-ambient');document.documentElement.removeAttribute('data-ambient');}
 }
 new MutationObserver(syncAmbient).observe(dock,{subtree:true,childList:true,attributes:true,attributeFilter:['data-boberto-fondo','href']});
 let attempts=0;
 function ready(){
  if(!window.BOBERTO||!window.BOBERTO.dock()){
   if(++attempts>200){post({type:'error',message:'Boberto did not adopt the console dock.'});return;}
   setTimeout(ready,50);return;
  }
  applyScale();syncAmbient();post({type:'ready',facts:window.desktopDock.facts()});
 }
 if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',()=>setTimeout(ready,0),{once:true});else setTimeout(ready,0);
})();
