{ Knuth's "man or boy" test, with procedural parameters and nested functions. }
program manorboy(output);
var k: integer;
function a(k: integer; function x1: integer; function x2: integer; function x3: integer;
           function x4: integer; function x5: integer): integer;
   function b: integer;
   begin k := k - 1; b := a(k, b, x1, x2, x3, x4) end;
begin
   if k <= 0 then a := x4 + x5 else a := b
end;
function one: integer; begin one := 1 end;
function minusone: integer; begin minusone := -1 end;
function zero: integer; begin zero := 0 end;
begin
   for k := 0 to 10 do write(a(k, one, minusone, minusone, one, zero):5);
   writeln
end.
