// 합성 이벤트 검사. 실제 화면 키보드 로그인 검사를 대체하지 않는다.
globalThis.runFocusRegression = () => {
 const results=[];
 const emit=(el,type,data)=>el.dispatchEvent(new CompositionEvent(type,{bubbles:true,data}));
 const start=(el,text)=>{emit(el,'compositionstart','');emit(el,'compositionupdate',text)};
 const expect=(name,values,extra=true)=>{const actual=qaRead(),expected=values.join('|');results.push({name,actual,expected,pass:actual===expected&&extra})};
 for(const [a,b,text] of [[0,1,'qaalpha'],[1,0,'synthetic'],[0,1,'가나'],[1,0,'한글'],[0,2,'alpha'],[2,0,'가 나'],[2,1,'line']]) {
  qaClear();qaFocus(a);const old=document.activeElement;start(old,text);qaFocus(b);emit(old,'compositionend',text);emit(old,'compositionend',text);
  const values=['','',''];values[a]=text;expect(`조합 전환 ${a}→${b}`,values);
 }
 qaClear();qaFocus(0);let el=document.activeElement;start(el,'alpha');emit(el,'compositionend','alpha');qaFocus(1);el=document.activeElement;start(el,'beta');emit(el,'compositionend','beta');expect('확정 후 새 조합',['alpha','beta','']);
 for(const [a,b,s,e] of [[0,1,7,10],[0,1,10,7],[1,0,7,10],[2,0,7,10]]) {
  qaClear();qaSeed(a,'prefix OLD suffix',s,e);const old=document.activeElement;start(old,'새');qaFocus(b);emit(old,'compositionend','새');const values=['','',''];values[a]='prefix 새 suffix';expect(`선택 교체 ${a}→${b} ${s}:${e}`,values);
 }
 for(const disabled of [false,true]) {
  qaClear();qaFocus(0);const old=document.activeElement;start(old,'blocked');qaFlags(0,!disabled,!disabled);qaFocus(1);emit(old,'compositionend','blocked');expect(disabled?'비활성화':'읽기전용',['','','']);qaFlags(0,false,true);
 }
 for(const [a,b] of [[0,1],[1,0],[0,2],[2,0]]) {
  qaClear();qaFocus(a);const old=document.activeElement;start(old,'alpha');qaFocus(b);const next=document.activeElement;start(next,'beta');emit(old,'compositionend','alpha');emit(next,'compositionend','beta');const values=['','',''];values[a]='alpha';values[b]='beta';expect(`이전 DOM 지연 종료 ${a}→${b}`,values,!old.isConnected);
 }
 return {passed:results.filter(x=>x.pass).length,total:results.length,results};
};
