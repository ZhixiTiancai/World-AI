'use strict';

const bridge = window.WorldNative;
const version = bridge.version();
const themes = {purple:'雾紫',red:'柔红',green:'灰绿',yellow:'浅金',blue:'雾蓝',black:'墨黑',white:'月白'};
const sections = ['Characters','Profiles','Presets','Models','Books','Sessions'];
const demoDelay = 22;
const callbacks = new Map();
const uid = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), value => value.toString(16).padStart(2,'0')).join('');
const dateValue = () => '/Date(' + Date.now() + ')/';
const item = (name, fields = {}) => Object.assign({Id:uid(),Name:name}, fields);
const find = (list, id) => list.find(value => value.Id === id);
const clone = value => JSON.parse(JSON.stringify(value));
const keywords = value => String(value || '').split(/[,，、;\n；]/).map(word => word.trim()).filter(Boolean);
const retrievalMode = value => ['semantic','both'].includes(value) ? value : 'keyword';
const snippet = (value, limit = 100) => {
    const text = String(value || '').replace(/\s+/g, ' ').trim();
    return text.length > limit ? text.slice(0, limit) + '…' : text;
};

function defaults() {
    return {ThemeId:'green',HistoryCount:40,ContextCharacters:0,PageSize:100,CheckUpdatesOnStart:true};
}

function seed() {
    const role = item('林渡', {Background:'住在雾港的信使。冷静、观察细致，熟悉码头。',
        Greeting:'林渡把一封受潮的信塞进你手里。“如果有人问起，你今晚没见过我。”', AlternateGreetings:[]});
    const book = item('雾港设定', {Entries:[item('灯塔',{Keywords:'灯塔, 灯光',Content:'旧灯塔封闭七年，连亮三次是旧航线的求救信号。',Enabled:true,Always:false})]});
    const demo = item('离线演示', {Demo:true});
    role.BookId = book.Id;
    const session = item('雾港来信', {CharacterId:role.Id,ModelId:demo.Id,Messages:[{Role:'assistant',Text:role.Greeting}],Memories:[],Updated:dateValue()});
    return {Version:1,Settings:defaults(),Characters:[role],Profiles:[],Presets:[],Models:[demo],Books:[book],Sessions:[session]};
}

function normalize(value) {
    if (!value || value.Version !== 1 || sections.some(key => !Array.isArray(value[key]))) {
        throw new Error('这不是可识别的 WorldAI 备份。');
    }
    const records = sections.flatMap(key => value[key]);
    if (records.some(entry => !entry || typeof entry.Id !== 'string' || typeof entry.Name !== 'string') ||
        new Set(records.map(entry => entry.Id)).size !== records.length) throw new Error('备份中包含无效记录。');
    value.Settings = Object.assign(defaults(), value.Settings);
    delete value.Settings.BackupDirectory;
    value.Settings.HistoryCount = integer(value.Settings.HistoryCount, 1, 500, 40);
    value.Settings.PageSize = integer(value.Settings.PageSize, 10, 500, 100);
    value.Settings.ContextCharacters = integer(value.Settings.ContextCharacters, 0, 500000, 0);
    value.Characters.forEach(role => {
        role.Background = mergePersonality(role.Background, role.Personality);
        role.Personality = null;
        role.AlternateGreetings = Array.isArray(role.AlternateGreetings)
            ? role.AlternateGreetings.filter(text => typeof text === 'string' && text.trim()) : [];
    });
    value.Models.forEach(model => delete model.EncryptedKey);
    value.Sessions.forEach(session => {
        if (!Array.isArray(session.Messages) || session.Messages.some(message => !message ||
            !['user','assistant'].includes(message.Role) || typeof message.Text !== 'string'))
            throw new Error('备份中包含无效消息。');
        session.Memories = Array.isArray(session.Memories) ? session.Memories.filter(text => typeof text === 'string') : [];
        session.Updated ||= dateValue();
        session.ChatBackground = normalizeBackground(session.ChatBackground);
    });
    value.Books.forEach(book => {
        if (!Array.isArray(book.Entries) || book.Entries.some(entry => !entry || typeof entry.Name !== 'string'))
            throw new Error('备份中包含无效世界书。');
        book.Entries.forEach(entry => entry.Retrieval=retrievalMode(entry.Retrieval));
    });
    return value;
}

function integer(value, min, max, fallback) {
    return Number.isInteger(value) && value >= min && value <= max ? value : fallback;
}

function mergePersonality(background, personality) {
    const base = String(background || '').trim();
    const extra = String(personality || '').trim();
    return !extra || base.includes(extra) ? base : base ? base + '\n\n性格：' + extra : extra;
}

function readState() {
    const data = bridge.load();
    if (data.startsWith('!')) throw new Error(data.slice(1));
    return data ? normalize(JSON.parse(data)) : seed();
}

function saveState(value) {
    const error = bridge.save(JSON.stringify(value));
    if (error) throw new Error(error);
}

function request(action, params = {}, chunk = () => {}) {
    const id = uid();
    const promise = new Promise((resolve, reject) => {
        callbacks.set(id, {resolve, reject, chunk});
        bridge.request(id, action, JSON.stringify(params));
    });
    return {promise, cancel: () => bridge.cancel(id)};
}

window.worldEvent = (id, kind, text) => {
    if (id === 'export') { notify(text, kind === 'error'); return; }
    if (id === 'backup-folder') {
        if (kind !== 'error' && route==='settings' && settingPage==='backup') render();
        notify(text,kind==='error');
        return;
    }
    const listener = callbacks.get(id);
    if (!listener) return;
    if (kind === 'chunk') { listener.chunk(text); return; }
    callbacks.delete(id);
    if (kind === 'done') listener.resolve(text);
    else {
        const error = new Error(text);
        error.cancelled = kind === 'cancel';
        listener.reject(error);
    }
};

function modelConfig(model, fields = {}) {
    return Object.assign({endpoint:model.Endpoint, name:model.ModelName, modelId:slot(model.Id,model.Endpoint),useSaved:true}, fields);
}

function player(world, session) {
    return find(world.Profiles, session.ProfileId ?? world.Settings.DefaultProfileId);
}

function expand(text, role, profile, now = new Date()) {
    const parts = {char:role?.Name || '角色', user:profile?.Name || '玩家', year:now.getFullYear(),
        month:now.getMonth()+1, day:now.getDate(), hour:now.getHours(), minute:now.getMinutes(), second:now.getSeconds()};
    return String(text || '').replace(/\{\{(char|user|year|month|day|hour|minute|second)\}\}/gi,
        (_, key) => String(parts[key.toLowerCase()]).padStart(2, '0'));
}

function loreEntries(world,session) {
    const role=find(world.Characters,session.CharacterId);
    const book=session.UseWorldBook!==false && find(world.Books,role?.BookId);
    return book ? book.Entries.filter(entry => entry.Enabled!==false && String(entry.Content || '').trim()) : [];
}

function keywordLore(world,session,recent) {
    const text=recent.slice(-6).map(message => message.Text.toLowerCase()).join('\n');
    return loreEntries(world,session).filter(entry => entry.Always ||
        (entry.Retrieval!=='semantic' && keywords(entry.Keywords).some(word => text.includes(word.toLowerCase()))));
}

const vectorCache=new Map();

async function semanticLore(world,session,recent,matched,control) {
    const used=new Set(matched.map(entry => entry.Id));
    const entries=loreEntries(world,session).filter(entry => ['semantic','both'].includes(entry.Retrieval) && !used.has(entry.Id));
    const latest=[...recent].reverse().find(message => message.Role==='user' && message.Text.trim());
    if (!entries.length || !latest) return [];
    const api=find(world.Models,world.Settings.SemanticApiId);
    const model=world.Settings.EmbeddingModel;
    if (!api || api.Demo || !model) throw new Error('请在世界书里配置语义检索。');
    if (vectorCache.size>2048) vectorCache.clear();
    const jobs=[];
    for (const entry of entries) {
        const chars=Array.from(entry.Content);
        for (let i=0;i<chars.length;i+=1600) {
            const text=chars.slice(i,i+1600).join('').trim();
            if (!text) continue;
            const key=JSON.stringify([api.Id,api.Endpoint,model,entry.Name,text]);
            jobs.push({entry,text,key});
            if (jobs.length>640) throw new Error('参与语义检索的设定太多，请把世界书拆小一些。');
        }
    }
    let current=null, stopped=false;
    const timer=setTimeout(() => {
        stopped=true;
        if (current) current.cancel();
    },40000);
    control.cancel=() => {
        control.aborted=true;
        if (current) current.cancel();
    };
    async function embed(input) {
        if (control.aborted || stopped) throw new Error('已停止检索。');
        current=request('embeddings',modelConfig(api,{name:model,input}));
        const rows=JSON.parse(await current.promise).data;
        if (!Array.isArray(rows) || rows.length!==input.length) throw new Error('向量接口未返回完整结果。');
        const vectors=new Map();
        for (const row of rows) {
            const index=row?.index, vector=row?.embedding;
            if (!Number.isInteger(index) || index<0 || index>=input.length || vectors.has(index) ||
                !Array.isArray(vector) || vector.length<2 || vector.length>8192 || vector.some(value => !Number.isFinite(value)))
                throw new Error('向量接口的响应格式不兼容。');
            vectors.set(index,vector);
        }
        return input.map((_,i) => vectors.get(i));
    }
    try {
        const missing=[...new Map(jobs.filter(job => !vectorCache.has(job.key)).map(job => [job.key,job])).values()];
        for (let i=0;i<missing.length;i+=16) {
            const batch=missing.slice(i,i+16);
            const vectors=await embed(batch.map(job => job.entry.Name+'\n'+job.text));
            batch.forEach((job,index) => vectorCache.set(job.key,vectors[index]));
        }
        const [query]=await embed([Array.from(latest.Text).slice(-1600).join('')]);
        const ranked=jobs.map(job => {
            const vector=vectorCache.get(job.key);
            if (vector.length!==query.length) { vectorCache.clear(); throw new Error('向量维度发生变化，请重试。'); }
            let dot=0, left=0, right=0;
            for(let i=0;i<query.length;i++){dot+=query[i]*vector[i];left+=query[i]*query[i];right+=vector[i]*vector[i];}
            return {...job,score:left && right ? dot/Math.sqrt(left*right) : 0};
        }).filter(job => job.score>=.42).sort((a,b) => b.score-a.score);
        const hits=[];
        for(const job of ranked) {
            if (used.has(job.entry.Id)) continue;
            used.add(job.entry.Id);
            hits.push({...job.entry,Content:job.text});
            if(hits.length===4) break;
        }
        return hits;
    } catch (error) {
        if (stopped) throw new Error('等待超时。');
        throw error;
    } finally { clearTimeout(timer); }
}

function buildContext(world, session, history, semantic = []) {
    const role = find(world.Characters, session.CharacterId);
    const profile = player(world, session);
    const preset = find(world.Presets, session.PresetId);
    const now = new Date();
    const segment = (label, text) => String(text || '').trim() ? '\n\n【' + label + '】\n' + String(text).trim() : '';
    const blocks = [['全局提示词',world.Settings.SystemPrompt],['扮演角色',role?.Name],
        ['背景与场景',role?.Background],['示例对话',role?.Examples],['玩家身份',profile?.Name],
        ['玩家背景',profile?.Description],['自定义指令',preset?.SystemPrompt],['写作风格',preset?.Style],
        ['回复长度',preset?.Length],['会话提示词',session.SystemPrompt],
        ['当前本地时间',session.SendTime ? now.toLocaleString('zh-CN') : '']];
    if (session.UseMemory !== false) blocks.push(['剧情摘要',session.Summary],['钉住的事件',session.Memories.join('\n')]);
    let system = expand('请用对话所用的语言进行角色扮演，延续场景。由玩家决定自己的行动与台词。' +
        blocks.map(([name,text]) => segment(name,text)).join(''), role, profile, now);
    const recent = history.slice(-(session.HistoryCount || world.Settings.HistoryCount));
    const limit = session.ContextCharacters ?? world.Settings.ContextCharacters;
    const lastLength = recent.length ? recent[recent.length-1].Text.length : 0;
    let left = limit ? limit - system.length - lastLength : Infinity;
    if (left < 0) throw new Error('提示词和最新消息超过发送字数上限，请调整会话设置。');
    let omitted = 0;
    for (const entry of [...keywordLore(world,session,recent),...semantic]) {
            const text = expand(segment('世界设定「'+entry.Name+'」',entry.Content),role,profile,now);
            if (text.length > left) { omitted++; continue; }
            system += text;
            left -= text.length;
    }
    let first = Math.max(0,recent.length-1);
    while (first > 0 && recent[first-1].Text.length <= left) left -= recent[--first].Text.length;
    return {messages:[{role:'system',content:system},...recent.slice(first).map(message => ({role:message.Role,content:message.Text}))],
        trimmed:first+omitted};
}

async function cardFile(file) {
    if (file.size > 32*1024*1024) throw new Error('文件超过 32 MB。');
    let raw = '', avatar = '';
    if (/\.png$/i.test(file.name) || file.type === 'image/png') {
        const bytes = new Uint8Array(await file.arrayBuffer());
        if ('137,80,78,71,13,10,26,10' !== String(bytes.slice(0,8))) throw new Error('PNG 文件无效。');
        const view = new DataView(bytes.buffer);
        const decoder = new TextDecoder();
        let legacy = '', v3 = '';
        for (let offset = 8; offset+12 <= bytes.length;) {
            const size = view.getUint32(offset);
            const start = offset+8;
            if (start+size+4 > bytes.length) throw new Error('PNG 文件不完整。');
            const kind = decoder.decode(bytes.slice(offset+4,start));
            if (kind === 'IEND') break;
            if (['tEXt','iTXt','zTXt'].includes(kind)) {
                const part = bytes.slice(start,start+size);
                const separator = part.indexOf(0);
                const name = separator < 0 ? '' : decoder.decode(part.slice(0,separator));
                if (name === 'chara' || name === 'ccv3') {
                    let rest = separator+1;
                    let compressed = kind === 'zTXt';
                    if (kind === 'zTXt') rest++;
                    if (kind === 'iTXt') {
                        compressed = part[rest] === 1;
                        rest += 2;
                        for (let n=0;n<2;n++) {
                            const end = part.indexOf(0,rest);
                            if (end < 0) throw new Error('PNG 卡片数据不完整。');
                            rest = end+1;
                        }
                    }
                    let payload = part.slice(rest);
                    if (compressed) {
                        if (!globalThis.DecompressionStream) throw new Error('当前系统 WebView 无法解压这张卡，请用 JSON 版。');
                        const input = new Blob([payload]).stream().pipeThrough(new DecompressionStream('deflate')).getReader();
                        const chunks = [];
                        let total = 0;
                        for (;;) {
                            const result = await input.read();
                            if (result.done) break;
                            total += result.value.length;
                            if (total > 8*1024*1024) { await input.cancel(); throw new Error('PNG 卡片数据过大。'); }
                            chunks.push(result.value);
                        }
                        payload = new Uint8Array(await new Blob(chunks).arrayBuffer());
                    }
                    if (name === 'ccv3') v3 = decoder.decode(payload);
                    else legacy = decoder.decode(payload);
                }
            }
            offset += size+12;
        }
        const data = (v3 || legacy).trim();
        if (!data) throw new Error('找不到 PNG 里的角色卡。');
        raw = data.startsWith('{') ? data : new TextDecoder().decode(Uint8Array.from(atob(data), char => char.charCodeAt(0)));
        avatar = await avatarFile(file);
    } else raw = await file.text();
    const parsed = JSON.parse(raw.replace(/^\uFEFF/,''));
    const data = parsed.data || parsed;
    if (typeof data.name !== 'string' || !data.name.trim()) throw new Error('角色卡缺少角色名。');
    const lore = data.character_book?.entries ? importBook(data.character_book,data.name+'的世界书') : null;
    const role = item(data.name.trim(), {Background:mergePersonality([data.description,data.scenario].filter(Boolean).join('\n\n'),data.personality),
        Greeting:String(data.first_mes || ''),Examples:String(data.mes_example || ''),
        AlternateGreetings:Array.isArray(data.alternate_greetings) ? data.alternate_greetings.filter(value => typeof value === 'string' && value.trim()) : [],
        Avatar:avatar || String(data.extensions?.worldai_avatar_png || ''),BookId:lore?.Id});
    return {role,lore};
}

async function imageCanvas(file, maxSide, sourceLimit) {
    if (file.size > sourceLimit * 1024 * 1024) throw new Error('请选择不超过 '+sourceLimit+' MB 的图片。');
    const url = URL.createObjectURL(file);
    try {
        const image = new Image();
        await new Promise((resolve,reject) => { image.onload=resolve; image.onerror=reject; image.src=url; });
        if (!image.width || !image.height || image.width*image.height>40000000)
            throw new Error('图片尺寸超出限制。');
        const canvas = document.createElement('canvas');
        const scale = Math.min(1,maxSide/Math.max(image.width,image.height));
        canvas.width=Math.max(1,Math.round(image.width*scale));
        canvas.height=Math.max(1,Math.round(image.height*scale));
        canvas.getContext('2d').drawImage(image,0,0,canvas.width,canvas.height);
        return canvas;
    } catch (error) { throw new Error(error.message || '无法读取图片，请换一张 PNG 或 JPG 图片。'); }
    finally { URL.revokeObjectURL(url); }
}

async function avatarFile(file) {
    const canvas = await imageCanvas(file,256,32);
    return canvas.toDataURL('image/png').split(',')[1];
}

function normalizeBackground(value) {
    if (!value || typeof value.Image !== 'string' || value.Image.length>2000000 || !value.Image.startsWith('/9j/'))
        return null;
    return {Image:value.Image,Shade:integer(value.Shade,0,90,45)};
}

async function backgroundFile(file) {
    const canvas = await imageCanvas(file,1920,24);
    for (const quality of [.86,.72,.56]) {
        const result = canvas.toDataURL('image/jpeg',quality);
        const image = result.slice(result.indexOf(',')+1);
        const value = normalizeBackground({Image:image,Shade:45});
        if (value) return value;
    }
    throw new Error('图片压缩后仍然太大，请换一张较小的图片。');
}

function importBook(value, name) {
    if (!value || typeof value.entries !== 'object' || value.entries === null) throw new Error('找不到可识别的世界书条目。');
    return item(typeof value.name === 'string' && value.name.trim() ? value.name : name, {Entries:Object.values(value.entries).map((entry,i) => {
        if (!entry || typeof entry !== 'object') throw new Error('世界书中有无效条目。');
        const keys = entry.keys ?? entry.key;
        return item(String(entry.comment || entry.name || '条目 '+(i+1)),{
            Keywords:Array.isArray(keys) ? keys.filter(word => typeof word === 'string').join(', ') : String(keys || ''),
            Content:String(entry.content || ''),Enabled:entry.disable !== true && entry.enabled !== false,Always:entry.constant === true,
            Retrieval:retrievalMode(entry.extensions?.worldai_retrieval)});
    })});
}

function bookExport(book) {
    const entries = {};
    book.Entries.forEach((entry,i) => entries[i] = {uid:i,key:keywords(entry.Keywords),keysecondary:[],
        comment:entry.Name,content:entry.Content || '',disable:entry.Enabled === false,constant:entry.Always === true,
        order:i,position:0,probability:100,useProbability:false,selective:false,displayIndex:i,
        extensions:{worldai_retrieval:retrievalMode(entry.Retrieval)}});
    return {name:book.Name,entries};
}

function roleExport(role, book) {
    const data = {name:role.Name,description:role.Background || '',personality:'',scenario:'',
        first_mes:role.Greeting || '',mes_example:role.Examples || '',alternate_greetings:role.AlternateGreetings,
        creator_notes:'',system_prompt:'',post_history_instructions:'',tags:[],creator:'',character_version:'',
        extensions:role.Avatar ? {worldai_avatar_png:role.Avatar} : {}};
    if (book) data.character_book = {name:book.Name,extensions:{},entries:book.Entries.map((entry,i) => ({
        id:i,name:entry.Name,keys:keywords(entry.Keywords),secondary_keys:[],enabled:entry.Enabled !== false,
        constant:entry.Always === true,content:entry.Content,insertion_order:i,selective:false,
        extensions:{worldai_retrieval:retrievalMode(entry.Retrieval)}}))};
    return {spec:'chara_card_v2',spec_version:'2.0',data};
}

function exportJson(name, entry) {
    bridge.exportFile(name.replace(/[<>:"/\\|?*]/g,'_')+'.json','application/json',JSON.stringify(entry,null,2));
}
