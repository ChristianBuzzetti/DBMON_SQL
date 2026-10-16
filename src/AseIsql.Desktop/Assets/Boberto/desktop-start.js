window.BOBERTO_HOST={tips:[]};
window.desktopErrors=[];
// ResizeObserver loop notices are benign browser warnings raised as error events; they are not script failures.
window.addEventListener('error',event=>{const message=String(event.message||'Boberto script error').slice(0,500);if(/^ResizeObserver loop/.test(message))return;window.desktopErrors.push(message);window.chrome?.webview?.postMessage({type:'error',message});});
