program gotononlocal(output);
label 99;
var level: integer;
procedure deep(n: integer);
   function f(k: integer): integer;
   begin
      if k = 3 then goto 99;
      f := k
   end;
begin
   level := n;
   if n < 5 then deep(n + 1)
   else writeln('f = ', f(1) + f(3):1)
end;
procedure local;
label 5;
var i: integer;
   procedure leave(k: integer); begin if k = 7 then goto 5 end;
begin
   for i := 1 to 100 do leave(i);
5: writeln('left the loop at ', i:1)
end;
begin
   level := 0;
   deep(1);
   writeln('not printed');
99: writeln('jumped out at level ', level:1);
   local;
   writeln('end')
end.
