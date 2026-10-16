'use strict';
function bootStudio(){
const studio=window.BOBERTO_ESTUDIO.montar({escena:document.getElementById('stage'),tabs:document.getElementById('tabs'),paneles:document.getElementById('panels'),fondo:document.getElementById('background'),marco:document.getElementById('frame'),rotulo:document.getElementById('look'),fabrica:document.getElementById('factory'),volver:document.getElementById('restore'),persistir:true,igualarAlto:false,escala:()=>{const stage=document.getElementById('stage');return Math.round(Math.max(7,Math.min(15,stage.clientHeight/30,stage.clientWidth/36)));}});
if(!studio)throw new Error('Boberto Studio did not initialize.');
window.desktopStudio={
 applyTheme(colors,theme,motion){const root=document.documentElement;for(const [name,value] of Object.entries(colors))root.style.setProperty(name,value);root.setAttribute('data-theme',theme);root.setAttribute('data-animations',motion===false?'off':'on');document.dispatchEvent(new Event('smg:theme-preview'));scheduleFrame();},
 facts(){const B=window.BOBERTO;return {ready:!!studio,gestures:B.listGestures().length,accessories:B.listHats().length,skins:B.listCharacters().length,backgrounds:B.listFondos().length,borders:B.listBordes().length,skin:B.getSkin(),hat:B.getHat(),character:B.getCharacter(),panels:document.querySelectorAll('#tabs [role=tab]').length};},
 teardown(){clearTimeout(frameTimer);studio.desmontar();window.BOBERTO.stopPreview();},
 reset(){window.BOBERTO.resetLook();exportFrame();}
};
let frameTimer;
function exportFrame(){const B=window.BOBERTO,canvas=document.createElement('canvas');B.renderPreview(canvas,4);const png=canvas.toDataURL('image/png');B.stopPreview();window.chrome?.webview?.postMessage({type:'avatar',png});}
function scheduleFrame(){clearTimeout(frameTimer);frameTimer=setTimeout(exportFrame,300);}
['boberto-skin-changed','boberto-fx-changed','boberto-fondo-changed','boberto-borde-changed'].forEach(name=>document.addEventListener(name,scheduleFrame));
window.addEventListener('pagehide',()=>window.desktopStudio.teardown(),{once:true});
// Choice cooldown: a look choice (background, accessory, border…) applies at once and the console dock reloads to show
// it; further choices wait COOLDOWN_MS so rapid clicking cannot stack dock reloads. Hover previews stay available.
const COOLDOWN_MS=1500;let coolUntil=0,coolTimer;
const panels=document.getElementById('panels');
function choiceOf(target){const chip=target instanceof Element?target.closest('button.bes-chip[aria-pressed]'):null;return chip&&panels.contains(chip)?chip:null;}
panels.addEventListener('click',event=>{
 const chip=choiceOf(event.target);if(!chip)return;
 if(performance.now()<coolUntil){event.preventDefault();event.stopImmediatePropagation();return;}
 if(chip.getAttribute('aria-pressed')==='true')return;
 coolUntil=performance.now()+COOLDOWN_MS;document.documentElement.setAttribute('data-cooldown','');
 clearTimeout(coolTimer);coolTimer=setTimeout(()=>document.documentElement.removeAttribute('data-cooldown'),COOLDOWN_MS);
},true);
window.desktopStudio.cooldownMs=COOLDOWN_MS;
window.chrome?.webview?.postMessage({type:'ready',facts:window.desktopStudio.facts()});scheduleFrame();

}
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',bootStudio,{once:true});else bootStudio();
