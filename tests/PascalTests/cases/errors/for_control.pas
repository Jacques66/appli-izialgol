program forcontrol(output);
var i, g: integer; r: real; a: array[1..2] of integer;
procedure p(k: integer);
var j, m: integer;
   procedure q;
   begin
      for m := 1 to 2 do      { m belongs to an intermediate block (Pascal 6000 error 155) }
   end;
begin
   for g := 1 to 2 do;        { a variable of the program is allowed }
   for k := 1 to 2 do;
   for j := 1 to 2 do j := 5
end;
procedure inc(var x: integer); begin x := x + 1 end;
begin
   for r := 1 to 2 do;
   for a[1] := 1 to 2 do;
   for i := 1 to 'z' do;
   for i := 1 to 10 do begin inc(i); read(i); for i := 1 to 2 do end
end.
