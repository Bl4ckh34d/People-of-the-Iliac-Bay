const $=id=>document.getElementById(id);
const shapes=[...'ABCDEFGHX'];
const gazes=['Oben links','Oben','Oben rechts','Links','Geradeaus','Rechts','Unten links','Unten','Unten rechts'];
const eyeLabels={open:'Augen offen',half:'Halb geschlossen',closed:'Augen geschlossen'};
const audio=$('speech');
let manifest,timeline,currentId='neutral',currentImages=null,selectionEpoch=0;
let mouth='X',cycle=false,cycleStart=0,eyeMode='auto',blinkStart=null,nextBlink=0;
let touring=false,tourIndex=0;
let thinking=false,gazeMode='auto',gaze=4,nextGaze=0;
const cache=new Map();
const sheetJobs=new WeakMap();
function status(text,error=false){$('status').textContent=text;$('status').classList.toggle('error',error)}
function loadImage(src){return new Promise((resolve,reject)=>{const img=new Image();img.onload=()=>resolve(img);img.onerror=()=>reject(Error('Bild fehlt: '+src));img.src=src})}
async function json(src){const r=await fetch(src);if(!r.ok)throw Error('Datei fehlt: '+src);return r.json()}
function paint(canvas,img,shape){const n=typeof shape==='number'?shape:shapes.indexOf(shape);if(n<0||n>8)throw Error('Unbekannte Bildzelle');const w=img.naturalWidth/3,h=img.naturalHeight/3,ctx=canvas.getContext('2d');ctx.imageSmoothingEnabled=false;ctx.clearRect(0,0,64,64);ctx.drawImage(img,(n%3)*w,Math.floor(n/3)*h,w,h,0,0,64,64)}
function imagesFor(emotion){if(!cache.has(emotion.id))cache.set(emotion.id,Promise.all(['open','half','closed'].map(async phase=>{const img=await loadImage(emotion.atlases[phase].file);if(img.naturalWidth!==img.naturalHeight||img.naturalWidth%3)throw Error('Ungültiges Raster: '+emotion.id+'/'+phase);return [phase,img]})).then(Object.fromEntries));return cache.get(emotion.id)}
async function selectEmotion(id){const epoch=++selectionEpoch;const emotion=manifest.emotions.find(e=>e.id===id);if(!emotion)throw Error('Unbekannte Emotion');status('Lade '+emotion.label+' …');const images=await imagesFor(emotion);if(epoch!==selectionEpoch)return false;thinking=false;currentId=id;currentImages=images;$('think').setAttribute('aria-pressed','false');$('live-label').textContent=emotion.label+' · animiert';for(const button of $('emotions').children)button.setAttribute('aria-pressed',String(button.dataset.emotion===id));status('Überarbeitete Sets · visuelle Beurteilung ausstehend.');return true}
async function startThinking(){const epoch=++selectionEpoch;stopTour();audio.pause();cycle=false;mouth='X';const images=await imagesFor(manifest.thinking);if(epoch!==selectionEpoch)return;thinking=true;currentImages=images;gaze=gazeMode==='auto'?4:Number(gazeMode);nextGaze=performance.now()+2500+Math.random()*4000;$('live-label').textContent='Nachdenklich · geschlossener Mund';$('think').setAttribute('aria-pressed','true');for(const button of $('emotions').children)button.setAttribute('aria-pressed','false');status('Nachdenkblicke · Richtungen aus Sicht des Betrachters.')}
function gazeAt(now){if(gazeMode!=='auto')return Number(gazeMode);if(now>=nextGaze){gaze=(gaze+1+Math.floor(Math.random()*8))%9;nextGaze=now+2500+Math.random()*4000}return gaze}
function nextBlinkTime(now){return now+2500+Math.random()*4500}
function eyesAt(now){if(blinkStart!==null){const elapsed=now-blinkStart;if(elapsed<65)return 'half';if(elapsed<165)return 'closed';if(elapsed<230)return 'half';blinkStart=null;nextBlink=nextBlinkTime(now)}if(eyeMode!=='auto')return eyeMode;if(now>=nextBlink){blinkStart=now;return 'half'}return 'open'}
function mouthAt(now){if(!audio.paused&&!audio.ended&&timeline){const sample=Math.floor(audio.currentTime*timeline.audioSampleRate);return (timeline.cues.find(c=>sample>=c.startSample&&sample<c.endSample)||{shape:'X'}).shape}if(cycle)return shapes[Math.floor((now-cycleStart)/650)%shapes.length];return mouth}
function frame(now){if(currentImages){const phase=eyesAt(now),shape=thinking?'X':mouthAt(now),cell=thinking?gazeAt(now):shape;paint($('live'),currentImages[phase],cell);$('live-state').textContent=(thinking?'Blick '+gazes[cell]:'Lautform '+shape)+' · '+eyeLabels[phase];Object.assign($('live').dataset,{emotion:thinking?'thinking':currentId,mouth:shape,eyes:phase,gaze:thinking?String(cell):''})}requestAnimationFrame(frame)}
function stopTour(){touring=false;$('tour').textContent='Alle Emotionen nacheinander mit Audio'}
async function tourTurn(){if(!touring)return;if(tourIndex>=manifest.emotions.length){stopTour();return}const ok=await selectEmotion(manifest.emotions[tourIndex].id);if(!ok||!touring)return;audio.currentTime=0;await audio.play()}
function report(error){stopTour();status(error.message||String(error),true)}
function renderSheet(details,emotion){if(sheetJobs.has(details))return sheetJobs.get(details);const job=(async()=>{details.dataset.loaded='loading';const images=await imagesFor(emotion);for(const phase of ['open','half','closed']){const section=document.createElement('section');section.className='phase';const title=document.createElement('h3');title.textContent=eyeLabels[phase];section.append(title);const grid=document.createElement('div');grid.className='grid';for(const [index,shape] of (emotion.id==='thinking'?gazes:shapes).entries()){const figure=document.createElement('figure');figure.className='tile';const canvas=document.createElement('canvas');canvas.width=canvas.height=64;canvas.dataset.frame=emotion.id+'/'+phase+'/'+shape;const caption=document.createElement('figcaption');caption.textContent=shape==='X'?'X · Ruhepose':shape;paint(canvas,images[phase],emotion.id==='thinking'?index:shape);figure.append(canvas,caption);grid.append(figure)}section.append(grid);details.append(section)}details.dataset.loaded='true';$('count').textContent=document.querySelectorAll('[data-frame]').length+' / '+manifest.frameCount+' Einzelbilder geladen'})();sheetJobs.set(details,job);return job}
async function renderOverview(){await Promise.all(manifest.emotions.map(async emotion=>{const figure=document.createElement('figure'),canvas=document.createElement('canvas'),caption=document.createElement('figcaption');canvas.width=canvas.height=64;canvas.dataset.overview=emotion.id;caption.textContent=emotion.label;figure.append(canvas,caption);$('overview').append(figure);const images=await imagesFor(emotion);paint(canvas,images.open,'X')}))}
async function init(){[manifest,timeline]=await Promise.all([json('manifest.json'),json('../sample.visemes.json')]);if(manifest.emotions.length!==6||manifest.frameCount!==189||!manifest.thinking)throw Error('Unvollständige Emotionsgalerie');const original=await loadImage('../tfac-077-original.png');$('original').getContext('2d').drawImage(original,0,0,64,64);for(const emotion of [...manifest.emotions,manifest.thinking]){if(emotion.id!=='thinking'){const button=document.createElement('button');button.textContent=emotion.label;button.dataset.emotion=emotion.id;button.setAttribute('aria-pressed','false');button.onclick=()=>{stopTour();selectEmotion(emotion.id).catch(report)};$('emotions').append(button)}const details=document.createElement('details'),summary=document.createElement('summary');details.dataset.emotion=emotion.id;summary.textContent=emotion.label+' · 27 Frames';details.append(summary);details.addEventListener('toggle',()=>{if(details.open&&details.dataset.loaded===undefined)renderSheet(details,emotion).catch(report)});$('sheets').append(details)}nextBlink=nextBlinkTime(performance.now());await selectEmotion('neutral');await renderOverview();requestAnimationFrame(frame)}
$('eyes').onchange=e=>{eyeMode=e.target.value;blinkStart=null;nextBlink=nextBlinkTime(performance.now())};
$('mouth').onchange=e=>{stopTour();audio.pause();cycle=false;mouth=e.target.value;if(thinking)selectEmotion(currentId).catch(report)};
$('blink').onclick=()=>{blinkStart=performance.now()};
$('cycle').onclick=()=>{stopTour();audio.pause();cycle=true;cycleStart=performance.now();if(thinking)selectEmotion(currentId).catch(report)};
$('rest').onclick=()=>{stopTour();audio.pause();cycle=false;mouth='X';$('mouth').value='X';if(thinking)selectEmotion(currentId).catch(report)};
$('tour').onclick=()=>{stopTour();touring=true;tourIndex=0;cycle=false;$('tour').textContent='Emotionen werden nacheinander abgespielt';tourTurn().catch(report)};
$('stop').onclick=()=>{stopTour();audio.pause();audio.currentTime=0;cycle=false;mouth='X'};
$('expand').onclick=async()=>{try{for(const [index,details] of [...$('sheets').children].entries()){details.open=true;await renderSheet(details,[...manifest.emotions,manifest.thinking][index])}status('Alle '+manifest.frameCount+' Einzelbilder geladen.')}catch(e){report(e)}};
audio.onplay=()=>{cycle=false;mouth='X';if(thinking)selectEmotion(currentId).catch(report)};
audio.onended=()=>{mouth='X';if(touring){tourIndex++;tourTurn().catch(report)}};
audio.onpause=()=>{if(!audio.ended)stopTour();mouth='X'};
$('gaze').onchange=e=>{gazeMode=e.target.value;startThinking().catch(report)};
$('think').onclick=()=>startThinking().catch(report);
init().catch(report);


