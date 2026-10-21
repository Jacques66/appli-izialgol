program forglobal(output);
{ The control variable may be a variable of the program used in a procedure (Pascal 6000);
  after the loop it is undefined (Revised Report 9.2.3.3) }
var g: integer; c: char;
procedure count(n: integer);
   procedure inner;
   begin
      for g := n downto 1 do write(g:2);
      writeln
   end;
begin
   for g := 1 to n do write(g:2);
   writeln;
   inner
end;
begin
   count(3);
   for c := 'x' to 'z' do write(c);
   writeln;
   writeln(g)
end.
