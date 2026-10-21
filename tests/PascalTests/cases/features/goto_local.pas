program gotolocal(output);
label 1, 2, 3, 10;
var i, j: integer;
begin
   i := 0;
1: i := i + 1;
   if i < 5 then goto 1;
   writeln('i = ', i:1);
   goto 2;
   writeln('skipped');
2: for i := 1 to 10 do
   begin
      for j := 1 to 10 do
         if i * j = 42 then goto 3;
   end;
3: writeln('found ', i:1, ' * ', j:1);
   i := 0;
   repeat
      i := i + 1;
      if odd(i) then goto 10;
      write(i:3);
10: until i >= 10;
   writeln
end.
