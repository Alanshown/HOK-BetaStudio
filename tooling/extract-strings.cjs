const fs=require('fs'),path=require('path'),Babel=require('../opendesign/mockups/studio-v2/vendor/babel.js');
const base=path.resolve(__dirname,'../opendesign/mockups/studio-v2');
const values=new Set();
for(const file of ['app.jsx','components.jsx','screens.jsx','dialogs.jsx','data.js']){
 const ast=Babel.transform(fs.readFileSync(path.join(base,file),'utf8'),{ast:true,code:false,presets:['react']}).ast;
 function walk(n){if(!n||typeof n!=='object')return;if(n.type==='StringLiteral'&&/[\u3400-\u9fff]/.test(n.value))values.add(n.value);for(const k of Object.keys(n))if(k!=='loc'&&k!=='extra')if(Array.isArray(n[k]))n[k].forEach(walk);else if(n[k]&&typeof n[k]==='object')walk(n[k]);}
 walk(ast);
}
console.log(JSON.stringify([...values],null,2));
