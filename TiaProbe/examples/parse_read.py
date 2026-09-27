# -*- coding: utf-8 -*-
import re, io, sys

path = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets\plc_blocks\Read.xml"
text = io.open(path, encoding="utf-8").read()

# 按 CompileUnit 切分网络
units = re.findall(r'<SW\.Blocks\.CompileUnit ID="\d+" CompositionName="CompileUnits">.*?</SW\.Blocks\.CompileUnit>', text, re.S)
print("网络总数:", len(units))

for i, u in enumerate(units):
    # 该网络内的 IN 索引、PT/DT 索引、实例DB、Part Move
    ins = re.findall(r'<Component Name="IN" AccessModifier="Array">\s*<Access Scope="LiteralConstant">\s*<Constant>\s*<ConstantType>DInt</ConstantType>\s*<ConstantValue>(\d+)</ConstantValue>', u)
    pt = re.findall(r'<Component Name="PT" AccessModifier="Array">\s*<Access Scope="LiteralConstant">\s*<Constant>\s*<ConstantType>DInt</ConstantType>\s*<ConstantValue>(\d+)</ConstantValue>', u)
    dt = re.findall(r'<Component Name="DT" AccessModifier="Array">\s*<Access Scope="LiteralConstant">\s*<Constant>\s*<ConstantType>DInt</ConstantType>\s*<ConstantValue>(\d+)</ConstantValue>', u)
    inst = re.findall(r'<Component Name="(ADC_DB_\d+|ADC_DB|ADC_DT_DB_\d+)" />', u)
    move = 'Name="Move"' in u
    src = re.findall(r'<Access Scope="GlobalVariable" UId="\d+">\s*<Symbol>\s*<Component Name="(PT\d+|DT\d+)" />', u)
    print("网络%02d: IN=%s PT=%s DT=%s 实例=%s MOVE=%s 源变量=%s" % (i+1, ins, pt, dt, inst, move, src))
