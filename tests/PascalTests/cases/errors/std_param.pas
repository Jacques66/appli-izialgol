program stdparam(output);
function apply(function f(x: real): real; x: real): real; begin apply := f(x) end;
function g(x: integer): real; begin g := x end;
function h(var x: real): real; begin h := x end;
function old(function f: real): real; begin old := f(1) end;
procedure pr(x: real); begin end;
begin
   writeln(apply(sin, 1.0));
   writeln(apply(g, 1.0));
   writeln(apply(h, 1.0));
   writeln(old(h));
   writeln(apply(pr, 1.0));
   writeln(apply(3, 1.0))
end.
