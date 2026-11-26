'use strict';

const page = document.getElementById('page');
const tabs = document.getElementById('tabs');
const overlay = document.getElementById('modal');
const notice = document.getElementById('notice');
const dialogs = [];
const drafts = new Map();
let world, loadError, selected = null, route = 'chat', settingPage = null, pendingChat = null, visibleMessages = null, toast;

function element(tag, text, className) {
    const node = document.createElement(tag);
    if (text !== undefined) node.textContent = text;
    if (className) node.className = className;
    return node;
}

function button(text, action, className) {
    const node = element('button',text,className);
    node.type = 'button';
    node.addEventListener('click', () => {
        try { Promise.resolve(action()).catch(error => notify(error.message,true)); }
        catch (error) { notify(error.message,true); }
    });
    return node;
}

function notify(text, error = false) {
    clearTimeout(toast);
    notice.textContent = text || '操作未完成。';
    notice.className = error ? 'error' : '';
    notice.hidden = false;
    toast = setTimeout(() => notice.hidden = true, error ? 5500 : 2800);
}

function commit(action, draw = true) {
    const old = JSON.stringify(world);
    try {
        action();
        saveState(world);
    } catch (error) {
        world = JSON.parse(old);
        if (draw) render();
        throw error;
    }
    if (draw) render();
}

function updateItem(list, current, next) {
    const values = world[list];
    if (!current) values.push(next);
    else {
        const index = values.findIndex(value => value.Id === current.Id);
        if (index === -1) throw new Error('找不到要更新的记录。');
        values[index] = next;
    }
}

function applyTheme() {
    const picked = Object.prototype.hasOwnProperty.call(themes,world.Settings.ThemeId) ? world.Settings.ThemeId : 'green';
    document.documentElement.dataset.theme = picked;
    bridge.bars(getComputedStyle(document.documentElement).getPropertyValue('--sidebar').trim(),picked === 'white');
}

function heading(title, ...buttons) {
    const row = element('div',undefined,'heading');
    row.append(element('h1',title));
    if (buttons.length) {
        const actions = element('div',undefined,'buttons');
        actions.append(...buttons);
        row.append(actions);
    }
    page.append(row);
}

function card(title, subtitle, actions = [], avatar) {
    const tile = element('section',undefined,'card');
    const prefix = element('div',undefined,'details');
    if (avatar) {
        const image = element('img',undefined,'avatar');
        image.alt = '';
        image.src = 'data:image/png;base64,'+avatar;
        prefix.append(image);
    }
    const words = element('div');
    words.append(element('h2',title));
    if (subtitle) words.append(element('p',subtitle,'muted'));
    prefix.append(words);
    tile.append(prefix);
    if (actions.length) {
        const tools = element('div',undefined,'buttons');
        tools.append(...actions);
        tile.append(tools);
    }
    page.append(tile);
    return tile;
}

function dialog(title, create) {
    const sheet = element('div',undefined,'sheet');
    const header = element('div',undefined,'sheet-head row');
    const body = element('div',undefined,'sheet-body');
    const foot = element('div',undefined,'sheet-foot');
    const close = () => {
        if (dialogs[dialogs.length-1] !== state) return;
        if (state.cancel) state.cancel();
        dialogs.pop();
        showDialog();
    };
    const state = {sheet,body,foot,close,cancel:null};
    header.append(element('h2',title),button('关闭',close));
    sheet.append(header,body,foot);
    dialogs.push(state);
    showDialog();
    create(state);
    return state;
}

function showDialog() {
    const active = dialogs[dialogs.length-1];
    overlay.replaceChildren(...(active ? [active.sheet] : []));
    overlay.hidden = !active;
}

function ask(title, text, accept) {
    dialog(title, modal => {
        modal.body.append(element('p',text));
        modal.foot.append(button('取消',modal.close),button('确认',() => { accept(); modal.close(); },'primary'));
    });
}

function field(modal, label, value = '', options = {}) {
    const row = element('label',undefined,'field');
    row.append(element('span',label));
    const input = element(options.rows ? 'textarea' : 'input');
    if (options.rows) input.rows = options.rows;
    else input.type = options.type || 'text';
    input.value = value ?? '';
    if (options.placeholder) input.placeholder=options.placeholder;
    row.append(input);
    modal.body.append(row);
    return input;
}

function choose(modal, label, options, value) {
    const row = element('label',undefined,'field');
    row.append(element('span',label));
    const input = element('select');
    options.forEach(([id,title]) => {
        const option = element('option',title);
        option.value = id;
        input.append(option);
    });
    if (options.some(([id]) => id === value)) input.value = value;
    row.append(input);
    modal.body.append(row);
    return input;
}

function check(modal, label, value) {
    const row = element('label',undefined,'check');
    const input = element('input');
    input.type = 'checkbox';
    input.checked = !!value;
    row.append(input,element('span',label));
    modal.body.append(row);
    return input;
}

const listOptions = (list, empty) => [
    ...(empty === undefined ? [] : [['',empty]]),
    ...list.map(entry => [entry.Id,entry.Name])
];
const required = (input, label) => {
    if (!input.value.trim()) throw new Error('请填写'+label+'。');
    return input.value.trim();
};

function saveButton(modal, onSave, label = '保存') {
    modal.foot.append(button('取消',modal.close),button(label,() => { onSave(); modal.close(); },'primary'));
}

function readFile(extensions, use) {
    const input = element('input');
    input.type = 'file';
    input.accept = extensions;
    input.hidden = true;
    function dispose() { input.remove(); }
    input.addEventListener('change', async () => {
        const file = input.files[0];
        try { if (file) await use(file); }
        catch (error) { notify(error.message,true); }
        finally { dispose(); }
    },{once:true});
    input.addEventListener('cancel',dispose,{once:true});
    document.body.append(input);
    input.click();
}

function go(target) {
    if (pendingChat) return;
    route = target;
    selected = null;
    settingPage = null;
    render();
    page.scrollTop=0;
}

function render() {
    if (!world) {
        page.replaceChildren(element('p',loadError || '无法显示数据。','error'));
        return;
    }
    page.className = route === 'chat' && selected ? 'thread' : '';
    page.replaceChildren();
    tabs.replaceChildren();
    [['chat','对话'],['roles','角色卡'],['books','世界书'],['settings','设置']].forEach(([id,title]) => {
        const control = button(title,() => go(id));
        if (id === route) control.setAttribute('aria-current','page');
        control.disabled = !!pendingChat;
        tabs.append(control);
    });
    if (route === 'chat') selected ? renderThread() : renderSessions();
    else if (route === 'roles') renderRoles();
    else if (route === 'books') renderBooks();
    else renderSettings();
}

function renderSessions() {
    heading('会话',button('＋ 新建',() => editSession(null),'primary'));
    const time = session => Number((String(session.Updated).match(/\/Date\((\d+)/) || [])[1]) || Date.parse(session.Updated) || 0;
    for (const session of [...world.Sessions].sort((a,b) => time(b)-time(a))) {
        const role = find(world.Characters,session.CharacterId);
        const last = session.Messages[session.Messages.length-1];
        card(session.Name,(role?.Name || '') + (last ? ' · '+snippet(last.Text,88) : ''),
            [button('打开',() => { selected=session.Id; visibleMessages=null; render(); })],role?.Avatar);
    }
    if (world.Sessions.length === 0) card('还没有会话','选一张角色卡开始。');
}

function editSession(old) {
    if (!world.Characters.length) { notify('请先创建或导入角色卡。',true); return; }
    const draft = old ? clone(old) : item('',{Updated:dateValue(),Messages:[],Memories:[]});
    dialog(old ? '会话设置' : '新建会话',modal => {
        const title = field(modal,'会话名称',draft.Name);
        const character = choose(modal,'角色',listOptions(world.Characters),draft.CharacterId);
        character.disabled=!!old;
        let greeting;
        if (!old) {
            greeting = choose(modal,'本次会话开场白',[],'');
            const preview = field(modal,'开场白预览','',{rows:3});
            preview.readOnly=true;
            const reload = () => {
                const role=find(world.Characters,character.value);
                greeting.replaceChildren();
                const texts=[];
                if (role?.Greeting) texts.push(['主开场白',role.Greeting]);
                (role?.AlternateGreetings || []).forEach((text,i) => texts.push(['备选 '+(i+1),text]));
                texts.push(['不使用开场白','']);
                for (const [label,text] of texts) {
                    const option=element('option',text ? label+' · '+snippet(text,36) : label);
                    option.value=text;
                    greeting.append(option);
                }
                preview.value=greeting.value;
            };
            reload();
            character.addEventListener('change',reload);
            greeting.addEventListener('change',() => preview.value=greeting.value);
        }
        const chosen = draft.ModelId ?? (old ? '' : world.Settings.DefaultModelId ||
            world.Models.find(entry => !entry.Demo)?.Id || world.Models[0]?.Id);
        const model = choose(modal,'模型',listOptions(world.Models,'未设置'),chosen);
        const currentPlayer = find(world.Profiles,world.Settings.DefaultProfileId);
        const options = listOptions(world.Profiles,'跟随全局身份'+(currentPlayer ? ' · '+currentPlayer.Name : ''));
        options.push(['no-profile','不使用玩家身份']);
        const profile = choose(modal,'玩家身份',options,draft.ProfileId ?? '');
        const preset = choose(modal,'提示词预设',listOptions(world.Presets,'无预设'),draft.PresetId);
        const prompt = field(modal,'本会话提示词',draft.SystemPrompt,{rows:3});
        const inherited = check(modal,'使用本会话独立的上下文限制',draft.HistoryCount != null || draft.ContextCharacters != null);
        const count = field(modal,'历史条数（1–500）',draft.HistoryCount ?? world.Settings.HistoryCount,{type:'number'});
        const limit = field(modal,'请求字数上限（0 不限）',draft.ContextCharacters ?? world.Settings.ContextCharacters,{type:'number'});
        const toggle = () => count.disabled=limit.disabled=!inherited.checked;
        inherited.addEventListener('change',toggle);
        toggle();
        const stream = check(modal,'流式显示回复',draft.StreamReplies !== false);
        const clock = check(modal,'在提示词加入当前时间',draft.SendTime);
        const memory = check(modal,'使用剧情记忆',draft.UseMemory !== false);
        const book = check(modal,'使用挂载的世界书',draft.UseWorldBook !== false);
        const font = field(modal,'聊天字号（10–32）',draft.MessageFontSize || 14,{type:'number'});
        const speaker = check(modal,'显示说话人名称',draft.ShowSpeaker !== false);
        const background = button(draft.ChatBackground ? '聊天背景 · 自定义图片' : '聊天背景 · 跟随主题',() =>
            editChatBackground(draft.ChatBackground,value => {
                draft.ChatBackground=value;
                background.textContent=value ? '聊天背景 · 自定义图片' : '聊天背景 · 跟随主题';
            }));
        modal.body.append(background);
        if (old) modal.foot.append(button('删除',() => ask('删除会话','删除这段对话和剧情记忆？',() => {
            commit(() => world.Sessions=world.Sessions.filter(session => session.Id!==old.Id),false);
            selected=null;
            closeAfterConfirm(modal);
            render();
        }),'danger'));
        saveButton(modal,() => {
            const role=find(world.Characters,character.value);
            draft.CharacterId=role.Id;
            draft.Name=title.value.trim() || role.Name+'的会话';
            draft.ModelId=model.value || null;
            draft.ProfileId=profile.value || null;
            draft.PresetId=preset.value || null;
            draft.SystemPrompt=prompt.value.trim();
            draft.HistoryCount=inherited.checked ? numberField(count,1,500,'历史条数') : null;
            draft.ContextCharacters=inherited.checked ? contextLimit(limit) : null;
            draft.StreamReplies=stream.checked;
            draft.SendTime=clock.checked;
            draft.UseMemory=memory.checked;
            draft.UseWorldBook=book.checked;
            draft.MessageFontSize=numberField(font,10,32,'聊天字号');
            draft.ShowSpeaker=speaker.checked;
            draft.Updated=dateValue();
            if (!old) draft.Messages=greeting.value ? [{Role:'assistant',Text:expand(greeting.value,role,player(world,draft))}] : [];
            commit(() => updateItem('Sessions',old,draft),false);
            selected=draft.Id;
            render();
        },old ? '保存' : '创建');
    });
}

function closeAfterConfirm(modal) {
    setTimeout(() => modal.close(),0);
}

function numberField(input,min,max,label) {
    const value=Number(input.value);
    if (!input.value.trim() || !Number.isInteger(value) || value<min || value>max)
        throw new Error(label+'请输入 '+min+' 到 '+max+' 之间的整数。');
    return value;
}

function contextLimit(input) {
    const value=numberField(input,0,500000,'发送字数上限');
    if (value && value<1000) throw new Error('发送字数上限填 0 或至少 1000。');
    return value;
}

function renderThread() {
    const session=find(world.Sessions,selected);
    if (!session) { selected=null; render(); return; }
    const role=find(world.Characters,session.CharacterId);
    const model=find(world.Models,session.ModelId);
    const header=element('div',undefined,'thread-head row');
    const label=element('div');
    label.append(element('h2',session.Name),element('small',model?.Name || '未设置模型'));
    const tools=element('div',undefined,'buttons');
    const back=button('返回',() => go('chat'));
    const settings=button('设置',() => editSession(session));
    const more=button('更多',() => threadActions(session));
    back.disabled=settings.disabled=more.disabled=!!pendingChat;
    tools.append(settings,more);
    header.append(back,label,tools);
    const history=element('div',undefined,'history');
    const recent=visibleMessages || world.Settings.PageSize;
    if (session.Messages.length>recent) {
        const older=button('加载更早的消息',() => {
            visibleMessages=recent+world.Settings.PageSize;
            render();
        });
        history.append(older);
    }
    session.Messages.slice(-recent).forEach((message,i) => {
        const line=element('article',undefined,'line'+(message.Role==='user' ? ' mine' : ''));
        const controls=element('div',undefined,'tools');
        const speaker=message.Role==='user' ? player(world,session)?.Name || '你' : role?.Name || '角色';
        if (session.ShowSpeaker!==false) controls.append(element('span',speaker));
        if (!pendingChat) controls.append(button('···',() => editMessage(session,session.Messages.length-Math.min(recent,session.Messages.length)+i)));
        line.append(controls,element('div',message.Text,'bubble'));
        line.lastElementChild.style.fontSize=(session.MessageFontSize || 14)+'px';
        history.append(line);
    });
    if (pendingChat) {
        const line=element('article',undefined,'line');
        const progress=element('div',pendingChat.text || '…','bubble');
        line.append(progress);
        history.append(line);
        pendingChat.view=progress;
        pendingChat.scroller=history;
    }
    const compose=element('div',undefined,'composer');
    const text=element('textarea');
    text.placeholder='写下你的行动或台词';
    text.rows=1;
    text.value=drafts.get(session.Id) || '';
    text.addEventListener('input',() => drafts.set(session.Id,text.value));
    const send=button(pendingChat ? '停止' : '发送',() => pendingChat ? pendingChat.cancel() : generate(session,text.value,false),'primary');
    compose.append(text,send);
    const scene=element('div',undefined,'thread-log');
    if (session.ChatBackground) {
        scene.append(wallpaperImage(session.ChatBackground));
        history.classList.add('with-wallpaper');
    }
    scene.append(history);
    page.append(header,scene,compose);
    requestAnimationFrame(() => history.scrollTop=history.scrollHeight);
}

function threadActions(session) {
    dialog('会话功能',modal => {
        const tools=element('div',undefined,'buttons');
        tools.append(button('重新生成',() => { modal.close(); return generate(session,'',true); }),
            button('剧情记忆',() => { modal.close(); editMemory(session); }),
            button('聊天背景',() => {
                modal.close();
                editChatBackground(session.ChatBackground,value => commit(() => session.ChatBackground=value));
            }),
            button('导出聊天记录',() => {
                const {ChatBackground,...textOnly}=session;
                exportJson(session.Name,textOnly);
            }));
        modal.body.append(tools);
    });
}

function editMessage(session,index) {
    const message=session.Messages[index];
    dialog('编辑消息',modal => {
        const text=field(modal,'消息正文',message.Text,{rows:7});
        modal.foot.append(button('删除',() => ask('删除消息','删除这条消息？',() => {
            commit(() => session.Messages.splice(index,1));
            closeAfterConfirm(modal);
        }),'danger'));
        saveButton(modal,() => commit(() => message.Text=required(text,'消息正文')));
    });
}

async function generate(session,value,retry) {
    if (pendingChat || (!retry && !value.trim())) return;
    const model=find(world.Models,session.ModelId);
    if (!model) throw new Error('请先在会话设置中选择模型。');
    if (retry && !session.Messages.some(message => message.Role==='user')) return;
    const replace=retry && session.Messages[session.Messages.length-1]?.Role==='assistant';
    const input=retry ? null : {Role:'user',Text:value.trim()};
    const history=replace ? session.Messages.slice(0,-1) : session.Messages.concat(input ? [input] : []);
    let payload=buildContext(world,session,history);
    if (input) {
        commit(() => { session.Messages.push(input); session.Updated=dateValue(); },false);
        drafts.delete(session.Id);
    }
    const requestState={text:'',view:null,scroller:null,aborted:false,cancel:() => {}};
    pendingChat=requestState;
    render();
    const chunk = fragment => {
        requestState.text+=fragment;
        if (!requestState.view) return;
        const scroll=requestState.scroller;
        const nearEnd=scroll.scrollHeight-scroll.scrollTop-scroll.clientHeight<55;
        requestState.view.textContent=requestState.text;
        if (nearEnd) scroll.scrollTop=scroll.scrollHeight;
    };
    try {
        if (model.Demo) {
            let cancelled=false;
            requestState.cancel=() => cancelled=true;
            const reply=history[history.length-1].Text.includes('灯塔')
                ? '林渡望向雾里。“三次。旧航线的人在求救。”他将锈钥匙放在栏杆上。“你要去看看吗？”'
                : '林渡侧头听了听。“你可以先拆信，或者跟我去看是谁把渡船推回来了。”';
            for (const part of reply.match(/.{1,3}/g)) {
                await new Promise(resolve => setTimeout(resolve,demoDelay));
                if (cancelled) break;
                chunk(part);
            }
        } else {
            const recent=history.slice(-(session.HistoryCount || world.Settings.HistoryCount));
            try {
                const matched=await semanticLore(world,session,recent,keywordLore(world,session,recent),requestState);
                if (matched.length) payload=buildContext(world,session,history,matched);
            } catch (error) {
                if (!requestState.aborted) notify('本轮跳过语义检索：'+error.message);
            }
            if (requestState.aborted) return;
            const reply=request('chat',modelConfig(model,{messages:payload.messages,stream:session.StreamReplies!==false}),chunk);
            requestState.cancel=reply.cancel;
            await reply.promise;
            if (!requestState.text.trim()) throw new Error('模型没有返回正文。');
        }
        if (payload.trimmed) notify('达到字数上限，已略过 '+payload.trimmed+' 项旧消息或世界书设定。');
    } catch (error) {
        if (!error.cancelled) notify(error.message,true);
    } finally {
        pendingChat=null;
        if (requestState.text.trim()) {
            const answer={Role:'assistant',Text:requestState.text};
            commit(() => {
                if (replace) session.Messages[session.Messages.length-1]=answer;
                else session.Messages.push(answer);
                session.Updated=dateValue();
            },false);
        }
        render();
    }
}

window.worldBack = () => {
    if (dialogs.length) { dialogs[dialogs.length-1].close(); return true; }
    if (pendingChat) { pendingChat.cancel(); return true; }
    if (route==='settings' && settingPage) { openSettingPage(null); return true; }
    if (selected || route!=='chat') { go('chat'); return true; }
    return false;
};

document.addEventListener('DOMContentLoaded',() => {
    document.getElementById('version').textContent='v'+version;
    try { world=readState(); applyTheme(); }
    catch (error) { loadError=error.message; }
    render();
    if (world?.Settings.CheckUpdatesOnStart) checkUpdates(true);
});
