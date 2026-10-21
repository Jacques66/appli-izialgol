program functions(output);
type color = (red, green); link = ^integer;
var p: link;
function next(c: color): color; begin if c = red then next := green else next := red end;
function upper(c: char): char; begin upper := chr(ord(c) - 32) end;
function positive(x: real): boolean; begin positive := x > 0 end;
function half(x: integer): real; begin half := x / 2 end;
function make(v: integer): link; var q: link; begin new(q); q^ := v; make := q end;
function sum(n: integer): integer;
var s, i: integer;
   procedure add(k: integer); begin s := s + k; if k = n then sum := s end;
begin s := 0; for i := 1 to n do add(i) end;
begin
   writeln(ord(next(red)):1, upper('q'), positive(-1.0), half(7):5:2);
   p := make(9); writeln(p^:1);
   writeln(sum(10):1)
end.
