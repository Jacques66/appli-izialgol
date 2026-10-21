program forwarddecl(output);
function isodd(n: integer): boolean; forward;
function iseven(n: integer): boolean;
begin if n = 0 then iseven := true else iseven := isodd(n - 1) end;
function isodd;
begin if n = 0 then isodd := false else isodd := iseven(n - 1) end;
procedure b(n: integer); forward;
procedure a(n: integer);
begin if n > 0 then begin write('a', n:1, ' '); b(n - 1) end end;
procedure b;
begin if n > 0 then begin write('b', n:1, ' '); a(n - 1) end end;
begin
   writeln(iseven(10), isodd(7), iseven(3));
   a(5); writeln
end.
