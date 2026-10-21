program procparams(output);
var total: integer;
procedure apply(procedure p(n: integer); k: integer);
var i: integer;
begin for i := 1 to k do p(i) end;
procedure accumulate(n: integer); begin total := total + n end;
procedure print(n: integer); begin write(n:3) end;
function twice(function f(x: integer): integer; x: integer): integer;
begin twice := f(f(x)) end;
function inc(x: integer): integer; begin inc := x + 1 end;
function sq(x: integer): integer; begin sq := x * x end;
function compose(function f(x: integer): integer; function g(x: integer): integer; x: integer): integer;
begin compose := f(g(x)) end;
procedure passon(procedure p(n: integer)); begin apply(p, 3) end;
function oldstyle(function f: integer; x: integer): integer; begin oldstyle := f(x) + f(x + 1) end;
procedure noparams(procedure q); begin q; q end;
procedure hello; begin write('hello ') end;
procedure local(n: integer);
   procedure show(k: integer); begin write(k * n:4) end;
begin apply(show, 3); writeln end;
begin
   total := 0; apply(accumulate, 10); writeln(total:1);
   apply(print, 5); writeln;
   writeln(twice(inc, 5):1, ' ', twice(sq, 3):1, ' ', compose(inc, sq, 4):1, ' ', compose(sq, inc, 4):1);
   passon(print); writeln;
   writeln(oldstyle(sq, 2):1);
   noparams(hello); writeln;
   local(10)
end.
